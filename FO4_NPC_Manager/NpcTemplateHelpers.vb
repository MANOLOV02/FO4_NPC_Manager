Imports System.Globalization
Imports System.IO
Imports System.Drawing
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports FO4_Base_Library
Imports MaterialLib
Imports NiflySharp
Imports NiflySharp.Blocks
Imports OpenTK.Mathematics
Imports FO4_Base_Library.Canon.CanonInterpretacion

''' <summary>Pure stateless NPC template-flag helpers extracted from MainForm (no instance state,
''' no UI). Real separate class (NOT a partial). See 61-perf-mainform-split.</summary>
Friend NotInheritable Class NpcTemplateHelpers
    Private Sub New()
    End Sub

    ' Conservative ACBS flag/category mappings, based on the historical FNV actor-template field
    ' categorization. The matching fields keep the same meaning in FO4/SSE, but this is not
    ' a claim that every other surfaced ACBS bit is non-inherited: those bits remain
    ' unclassified until measured.
    Friend Const TraitsAcbsFlagsMask As UInteger = &H1UI
    Friend Const BaseDataAcbsFlagsMask As UInteger = &HAUI
    Friend Const StatsAcbsFlagsMask As UInteger = &H90UI
    Friend Const ClassifiedAcbsFlagsMask As UInteger = TraitsAcbsFlagsMask Or BaseDataAcbsFlagsMask Or StatsAcbsFlagsMask

    Public Shared Function HasTemplateFlag(flags As UShort, category As NPC_TemplateCategory) As Boolean
        Dim mask = CUShort(1 << CInt(category))
        Return (flags And mask) <> 0US
    End Function

    Public Shared Function ResolveTemplateSourceFormID(npc As NPC_Data, category As NPC_TemplateCategory) As UInteger
        Dim specificFormID = npc.Record.ActorDePlantilla(category)
        If specificFormID <> 0UI Then Return specificFormID

        Return npc.Record.Plantilla()
    End Function

    ''' <summary>The DISTINCT leaf NPC_ FormIDs an LVLN can yield, recursing into nested LVLNs. Weights,
    ''' Count and ChanceNone are deliberately ignored: the only question here is "how many DIFFERENT actors
    ''' could come out of this list", which is what decides whether collapsing it is deterministic.
    ''' <para>Feeds <see cref="NpcTemplateMaterializer.MakeCategoryOwn"/>. The caller pins the leaf currently
    ''' selected for preview, including a multi-leaf LVLN, when making a generic actor concrete. Returns an
    ''' empty list for a missing record or a non-LVLN signature, which the caller
    ''' treats as "unresolvable" (the conservative branch).</para></summary>
    Public Shared Function CollectLvlnLeafNpcFormIDs(lvlnFormID As UInteger, pluginManager As PluginManager) As List(Of UInteger)
        Dim leaves As New List(Of UInteger)
        If pluginManager Is Nothing OrElse lvlnFormID = 0UI Then Return leaves
        Dim seenLists As New HashSet(Of UInteger)
        Dim seenLeaves As New HashSet(Of UInteger)
        CollectLvlnLeavesRecursive(lvlnFormID, pluginManager, leaves, seenLeaves, seenLists)
        Return leaves
    End Function

    Private Shared Sub CollectLvlnLeavesRecursive(lvlnFormID As UInteger, pluginManager As PluginManager,
                                                  leaves As List(Of UInteger), seenLeaves As HashSet(Of UInteger),
                                                  seenLists As HashSet(Of UInteger))
        ' seenLists guards nested-LVLN cycles; without it a self-referencing list recurses forever.
        If Not seenLists.Add(lvlnFormID) Then Return
        Dim rec = pluginManager.GetRecord(lvlnFormID)
        If rec Is Nothing OrElse rec.Header.Signature <> "LVLN" Then Return
        ' Tolerante: lo consume el editor y el apply del Save; un LVLN roto no puede reventar ahi.
        Dim lvln = TryAbrirLvlnTolerante(rec, pluginManager)
        If lvln Is Nothing Then Return
        For Each entry In lvln.LeveledListEntries
            If entry.LeveledListEntryNPC = 0UI Then Continue For
            Dim entryRec = pluginManager.GetRecord(entry.LeveledListEntryNPC)
            If entryRec Is Nothing Then Continue For
            Select Case entryRec.Header.Signature
                Case "NPC_"
                    If seenLeaves.Add(entry.LeveledListEntryNPC) Then leaves.Add(entry.LeveledListEntryNPC)
                Case "LVLN"
                    CollectLvlnLeavesRecursive(entry.LeveledListEntryNPC, pluginManager, leaves, seenLeaves, seenLists)
            End Select
        Next
    End Sub

    ''' <summary>Envoltorio TOLERANTE de Canon.CanonRecords.Lvln: reemplaza a RecordParsers.TryParseLVLN
    ''' para los caminos de lectura/display (ver el comentario original en RecordParsers.vb sobre por
    ''' que la tolerancia va centralizada en un solo lugar y no repetida Try por Try en cada llamador).
    ''' Publico porque MainForm/NpcStateResolver/NpcOverrideSaver comparten el mismo contrato.</summary>
    Public Shared Function TryAbrirLvlnTolerante(rec As PluginRecord, pluginManager As PluginManager) As Canon.ILvln
        Try
            Return Canon.CanonRecords.Lvln(rec, pluginManager)
        Catch ex As Exception
            Logger.LogLazy(Function() $"[LVLN] {rec.SourcePluginName}:{rec.Header.FormID:X8} no parsea " &
                                      $"({ex.GetType().Name}: {ex.Message}); se saltea.")
            Return Nothing
        End Try
    End Function

    ''' <summary>True if this NPC inherits its visual appearance (Traits or ModelAnimation) from any template.
    ''' Such NPCs are generic — their look is defined by the template chain, not by themselves.</summary>
    Public Shared Function NpcInheritsVisualAppearance(npc As NPC_Data) As Boolean
        If npc Is Nothing Then Return False
        ' UNA lectura del campo, no tres. Lo llama el filtro de categorías por cada NPC y en cada
        ' repoblado del árbol, o sea una vez por tecla del buscador.
        Dim flags = npc.Record.ConfigurationTemplateFlags
        If flags = 0US Then Return False
        Return HasTemplateFlag(flags, NPC_TemplateCategory.Traits) OrElse
               HasTemplateFlag(flags, NPC_TemplateCategory.ModelAnimation)
    End Function

End Class
