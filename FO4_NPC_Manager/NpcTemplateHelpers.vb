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

    ''' <summary>⛔⛔ Los bits de ACBS que el motor copia por categoria. MEDIDOS en los dos binarios.
    ''' <para>Aca habia tres constantes que su propio comentario declaraba sin medir — salian de *«the
    ''' historical FNV actor-template field categorization»* y decia que los demas bits *«remain
    ''' unclassified until measured»*. Ya estan medidos, con censo completo de toda operacion sobre
    ''' `[TESActorBaseData+8]` (= ACBS Flags u32, file offset 0) dentro de la funcion de copia.</para>
    ''' <para>⛔ El VALOR no vive aca: vive en el `.exe`. Estas constantes son la DECISION de la app, y
    ''' `BucketTraitsGate` las contradice leyendo el inmediato del binario en la direccion citada
    ''' (DECISION 38). Si alguna esta mal, el gate se pone ROJO — no hay que acordarse de nada.</para>
    ''' <para>⛔ Y la mascara de Base Data <b>NO es la misma en los dos juegos</b>: SSE tiene el bit 15
    ''' que FO4 no tiene, FO4 tiene el 23 que SSE no tiene. Por eso deja de poder ser una `Const`.</para></summary>
    Friend Const TraitsAcbsFlagsMask As UInteger = &H80001UI   ' SSE 0x1403C20DB + 0x1403C20F7 / FO4 0x1406582B4 + 0x1406582D1

    ''' <summary>Los bits de Stats que se copian TAL CUAL. ⛔ El `0x10` NO esta aca a proposito: no es
    ''' una mascara sino una REGLA — ver <see cref="AplicarReglaAutoCalc"/>.</summary>
    Friend Const StatsAcbsFlagsMaskPlana As UInteger = &H40080UI   ' SSE 0x1403C236E + 0x1403C243F / FO4 0x1406585DC + 0x14065867C

    ''' <summary>⛔ El bit *tiene sonidos propios* (FO4 lo nombra **Has Base Sound Data**). NO es un
    ''' bit copiable: es el TITULO DE PROPIEDAD del contenedor de sonidos, y lo prenden y apagan las
    ''' propias rutinas del canal — `sub_1403C6250` / `sub_14065FC10` lo ponen (`0x1403C6288` /
    ''' `0x14065FC3F`), `sub_14065FB60` lo apaga (`0x14065FBD7`). Por eso no esta en ninguna mascara:
    ''' se DERIVA de si el heredero termino con sonidos propios o compartidos.</summary>
    Friend Const AcbsBitSonidosPropios As UInteger = &H100UI

    ''' <summary>El bit que el motor DERIVA en vez de copiar, y el que decide esa derivacion.</summary>
    Friend Const AcbsBitAutoCalc As UInteger = &H10UI      ' SSE thunk 0x1403AFEFE / FO4 0x1406448FE
    Friend Const AcbsBitPcLevelMult As UInteger = &H80UI

    ''' <summary>⛔ La mascara de Base Data es un dato POR JUEGO. SSE `0x1403C208F and edx,0x10A84A`;
    ''' FO4 `0x140658235 and edx,0x90284A`. Coinciden en los bits 1, 3, 6, 11, 13 y 20; SSE suma el 15
    ''' y FO4 el 23.</summary>
    Friend Shared Function BaseDataAcbsFlagsMask(esSse As Boolean) As UInteger
        Return If(esSse, &H10A84AUI, &H90284AUI)
    End Function

    ''' <summary>⛔ Los bits que la MATERIALIZACION deriva, y que por lo tanto **no** se pueden pisar
    ''' con un valor de antes de materializar. Hoy es uno solo: el <c>0x100</c>, que sale de si el
    ''' heredero termino con sonidos propios o compartidos (ver <c>CopiarSonidos</c>).
    ''' <para>Existe porque el guardado escribia la palabra ENTERA del snapshot pre-materializacion y
    ''' se llevaba puesto el bit derivado, dejando un record donde el grupo de sonidos y su bit se
    ''' contradicen.</para></summary>
    Friend Const AcbsBitsDerivadosPorMaterializacion As UInteger = AcbsBitSonidosPropios

    ''' <summary>Todos los bits cuya categoria de plantilla esta MEDIDA.
    ''' <para>⚠️ Un bit que no este aca es un bit que el motor no copia con una MASCARA — pero eso ya
    ''' no quiere decir "editarlo es siempre seguro": el <c>0x100</c> no esta en ninguna mascara y aun
    ''' asi la materializacion lo DERIVA (ver <see cref="AcbsBitsDerivadosPorMaterializacion"/>). La
    ''' frase anterior decia lo contrario y era falsa desde que se materializa el canal de sonidos.</para></summary>
    Friend Shared Function ClassifiedAcbsFlagsMask(esSse As Boolean) As UInteger
        Return TraitsAcbsFlagsMask Or StatsAcbsFlagsMaskPlana Or AcbsBitAutoCalc Or BaseDataAcbsFlagsMask(esSse)
    End Function

    ''' <summary>⛔⛔ El bit `0x10` (Auto-calc stats) NO se copia con una mascara: el motor decide.
    ''' <para>Medido en los dos, por un par getter/setter <b>virtual</b> sobre `TESNPC` que un censo de
    ''' escrituras directas no ve — SSE slots `+0x1F0`/`+0x1F8` (`0x1403AFED0`/`0x1403AFEF0`), FO4
    ''' `+0x260`/`+0x268` (`0x1406448D0`/`0x1406448F0`):</para>
    ''' <code>
    ''' eax = flags DEL DESTINO ; shr 7 ; test al,1     &lt;- el bit 0x80 YA fusionado unas lineas antes
    ''' si esta puesto  -&gt; setter(dest, TRUE)            (forzado; el origen no participa)
    ''' si no           -&gt; setter(dest, getter(ORIGEN))
    ''' </code>
    ''' <para>Como el `0x80` se fusiona incondicionalmente antes, la condicion es identicamente
    ''' `origen.bit7`, asi que la ley se escribe como funcion pura del ORIGEN y <b>no depende del
    ''' orden</b>: no hay nada que enhebrar.</para>
    ''' <para>SSE `0x1403C23FB`-`0x1403C242A` · FO4 `0x140658651`-`0x14065866F`.</para></summary>
    Friend Shared Function AplicarReglaAutoCalc(actual As UInteger, origen As UInteger) As UInteger
        If (origen And AcbsBitPcLevelMult) <> 0UI Then Return actual Or AcbsBitAutoCalc
        Return (actual And Not AcbsBitAutoCalc) Or (origen And AcbsBitAutoCalc)
    End Function

    Public Shared Function HasTemplateFlag(flags As UShort, category As NPC_TemplateCategory) As Boolean
        Return (flags And MascaraDePlantilla(category)) <> 0US
    End Function

    ''' <summary>⛔⛔ LA MASCARA DEL BIT, EN UN SOLO LUGAR: `1 << categoria`.
    ''' <para>Existe porque escribir la mascara a mano ya produjo un verde FALSO: un caso sembraba el bit con
    ''' `flags Or CUInt(NPC_TemplateCategory.Traits)` y, como `Traits = 0`, eso es `Or 0` -- no encendia NADA.
    ''' El caso pasaba igual (en un juego por casualidad, en el otro con la ley revertida). El lector ya tenia
    ''' la mascara; le faltaba el hermano que la ESCRIBE.</para></summary>
    Public Shared Function MascaraDePlantilla(category As NPC_TemplateCategory) As UShort
        Return CUShort(1 << CInt(category))
    End Function

    ''' <summary>⛔ Prende o apaga el bit de una categoria. Devuelve las banderas nuevas -- no muta: quien
    ''' tenga el record decide si se lo escribe.</summary>
    Public Shared Function PonerBanderaDePlantilla(flags As UShort, category As NPC_TemplateCategory,
                                                  encendido As Boolean) As UShort
        Dim m = MascaraDePlantilla(category)
        Return If(encendido, CUShort(flags Or m), CUShort(flags And Not m))
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
