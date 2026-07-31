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

''' <summary>Pure stateless NPC template-flag helpers extracted from MainForm (no instance state,
''' no UI). Real separate class (NOT a partial). See 61-perf-mainform-split.</summary>
Friend NotInheritable Class NpcTemplateHelpers
    Private Sub New()
    End Sub

    Public Shared Function HasTemplateFlag(flags As UShort, category As NPC_TemplateCategory) As Boolean
        Dim mask = CUShort(1 << CInt(category))
        Return (flags And mask) <> 0US
    End Function

    Public Shared Function ResolveTemplateSourceFormID(npc As NPC_Data, category As NPC_TemplateCategory) As UInteger
        Dim specificFormID As UInteger = 0UI
        If npc.TemplateActorFormIDs.TryGetValue(category, specificFormID) AndAlso specificFormID <> 0UI Then
            Return specificFormID
        End If

        Return npc.TemplateFormID
    End Function

    ''' <summary>The DISTINCT leaf NPC_ FormIDs an LVLN can yield, recursing into nested LVLNs. Weights,
    ''' Count and ChanceNone are deliberately ignored: the only question here is "how many DIFFERENT actors
    ''' could come out of this list", which is what decides whether collapsing it is deterministic.
    ''' <para>Feeds <see cref="NpcTemplateMaterializer.MakeCategoryOwn"/>, which materializes a one-leaf LVLN
    ''' and REFUSES a multi-leaf one — see that method for why a random pick must never be frozen into a
    ''' saved record. Returns an empty list for a missing record or a non-LVLN signature, which the caller
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
        Dim lvln = RecordParsers.ParseLVLN(rec, pluginManager)
        For Each entry In lvln.Entries
            If entry.FormID = 0UI Then Continue For
            Dim entryRec = pluginManager.GetRecord(entry.FormID)
            If entryRec Is Nothing Then Continue For
            Select Case entryRec.Header.Signature
                Case "NPC_"
                    If seenLeaves.Add(entry.FormID) Then leaves.Add(entry.FormID)
                Case "LVLN"
                    CollectLvlnLeavesRecursive(entry.FormID, pluginManager, leaves, seenLeaves, seenLists)
            End Select
        Next
    End Sub

    ''' <summary>True if this NPC inherits its visual appearance (Traits or ModelAnimation) from any template.
    ''' Such NPCs are generic — their look is defined by the template chain, not by themselves.</summary>
    Public Shared Function NpcInheritsVisualAppearance(npc As NPC_Data) As Boolean
        If npc Is Nothing OrElse npc.TemplateFlags = 0US Then Return False
        Return HasTemplateFlag(npc.TemplateFlags, NPC_TemplateCategory.Traits) OrElse
               HasTemplateFlag(npc.TemplateFlags, NPC_TemplateCategory.ModelAnimation)
    End Function

End Class
