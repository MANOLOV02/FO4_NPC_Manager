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

''' <summary>Pure stateless formatting / label helpers extracted from MainForm (no instance state,
''' no UI, no MainForm fields). Real separate class — NOT a partial of MainForm. Part of slimming
''' MainForm.vb; see 61-perf-mainform-split. Call sites use the qualified <c>NpcManagerFormat.X</c>.</summary>
Friend NotInheritable Class NpcManagerFormat
    Private Sub New()
    End Sub

    Public Shared Function DescribeNpc(npc As NPC_Data) As String
        If npc Is Nothing Then Return "<unknown NPC>"
        If npc.EditorID <> "" Then Return npc.EditorID
        If npc.Record.Name <> "" Then Return npc.Record.Name
        Return npc.FormID.ToString("X8")
    End Function

    Public Shared Function DescribeRecord(rec As PluginRecord) As String
        If rec Is Nothing Then Return "<unknown record>"
        If rec.EditorID <> "" Then Return rec.EditorID
        Return $"{rec.Header.Signature} {rec.Header.FormID:X8}"
    End Function

    Public Shared Sub DeduplicateWarnings(warnings As List(Of String))
        If warnings Is Nothing OrElse warnings.Count <= 1 Then Return
        Dim unique = warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
        warnings.Clear()
        warnings.AddRange(unique)
    End Sub

    Public Shared Function BuildWarningSuffix(warnings As IList(Of String)) As String
        If warnings Is Nothing OrElse warnings.Count = 0 Then Return ""
        Return $" ({warnings(0)})"
    End Function

''' <summary>Etiqueta del clip para el combo de la barra de animacion.
''' <para>⛔ El orden es: insignias → nombre → variante → roles → 1a persona. Las insignias y la variante
''' van ANTES de los corchetes para que dos entradas del MISMO archivo se distingan sin tener que leer
''' hasta el final de una linea larga — que es exactamente el caso que el dedup por variante crea.</para>
''' <para>⛔ `VarianteSufijo` se calcula UNA vez por lista en el enumerador, no aca: esta funcion es Shared
''' sobre UN clip y el combo y el picker ven listas DISTINTAS (el picker filtra por genero, 1a persona y
''' texto), asi que calcularlo aca daria dos nombres para el mismo clip y ademas parpadearia al tipear en
''' el filtro. Leer un campo ya calculado es gratis.</para></summary>
    Public Shared Function AnimClipLabel(c As ResolvedAnimationClip) As String
        Dim nm = If(String.IsNullOrWhiteSpace(c.ClipName), System.IO.Path.GetFileNameWithoutExtension(c.AnimationFile), c.ClipName)
        ' ⚠ = el crop declarado no se puede honrar y el clip se reproduce entero. Mismo lexico que el
        ' picker (AnimationPicker_Form.Insignias). El guard de HkxFlagsKnown evita afirmar que esta bien
        ' antes de que la pasada lazy haya leido el .hkx.
        Dim ins = If(c.IsAdditive, "⊕ ", "") & If(c.HkxFlagsKnown AndAlso c.CropIgnorado, "⚠ ", "")
        Dim roles = If(c.Roles.Count > 0, $"  [{String.Join(",", c.Roles)}]", "")
        Dim fp = If(c.Is1stPersonOnly, "  · 1st-person", "")
        Return $"{ins}{nm}{c.VarianteSufijo}{roles}{fp}"
    End Function

    Public Shared Function GetTemplateCategoryLabel(category As NPC_TemplateCategory) As String
        Select Case category
            Case NPC_TemplateCategory.AIData
                Return "AI Data"
            Case NPC_TemplateCategory.AIPackages
                Return "AI Packages"
            Case NPC_TemplateCategory.ModelAnimation
                Return "Model/Animation"
            Case NPC_TemplateCategory.BaseData
                Return "Base Data"
            Case NPC_TemplateCategory.DefaultPackageList
                Return "Default Package List"
            Case Else
                Return category.ToString()
        End Select
    End Function

    Public Shared Function DescribeModelFlags(b As Byte) As String
        If b = 0 Then Return "none"
        Dim parts As New List(Of String)
        If (b And &H1) <> 0 Then parts.Add("FaceBones")
        If (b And &H2) <> 0 Then parts.Add("1stPerson")
        Dim extra = b And Not CByte(&H3)
        If extra <> 0 Then parts.Add($"unk0x{extra:X2}")
        Return String.Join("|", parts)
    End Function

    Public Shared Function GetHeadPartTypeName(partType As Integer) As String
        Select Case partType
            Case 0 : Return "Misc"
            Case 1 : Return "Face"
            Case 2 : Return "Eyes"
            Case 3 : Return "Hair"
            Case 4 : Return "Facial Hair"
            Case 5 : Return "Scar"
            Case 6 : Return "Eyebrows"
            Case 7 : Return "Meatcaps"
            Case 8 : Return "Teeth"
            Case 9 : Return "Head Rear"
            Case Else : Return $"Type{partType}"
        End Select
    End Function

    ' ========================================================================
    ' NPC_ record-details labels. Enum names and flag bits are transcribed from the game's own
    ' record schema, which is the authoritative source for both games. Where the two engines
    ' disagree the formatter takes the game and branches — the NPC_ record layout is NOT shared
    ' (ver Canon.INpc.ConfigurationFlags y el DNAM de Skyrim del record).
    ' ========================================================================

    ''' <summary>Un campo de banderas, escrito con el nombre de cada bit PRENDIDO.
    '''
    ''' <para>⛔ LOS NOMBRES SALEN DEL ESQUEMA, no de una tabla escrita acá. Acá había un
    ''' <c>Dictionary(Of UInteger, String)</c> a mano para ACBS con 19 de los 32 bits que Fallout 4
    ''' declara: los otros 13 salían como <c>unk0x400</c>, y el bit 19 salía con el nombre de Skyrim
    ''' («Opposite Gender Anims») cuando en Fallout es «Swap Gender Anims» — o sea que el panel
    ''' mostraba un nombre EQUIVOCADO, que es peor que no mostrar ninguno. El formato ya declara los
    ''' 32 nombres por juego (<c>WbIntegerDef.FlagNames</c>), así que la tabla a mano no sumaba nada
    ''' y se podía quedar vieja sin que nadie se enterara.</para>
    '''
    ''' <para>Sirve para CUALQUIER campo de banderas del record, no sólo ACBS: los flags de etapa de
    ''' destrucción, los de ataque y los de plantilla se piden igual, con su ruta.</para></summary>
    ''' <summary>El campo de banderas se busca por (FIRMA del subrecord, NOMBRE del campo).
    '''
    ''' <para>⛔ NO POR RUTA COMPLETA, y no es preferencia: las rutas del esquema llevan el nombre del
    ''' CONTENEDOR, que no se adivina —<c>«Attack\ATKD\Attack Data\Attack Flags»</c>,
    ''' <c>«Stage\DSTD\Destruction Stage Data\Flags»</c>—, se comparan con
    ''' <c>StringComparison.Ordinal</c>, y una ruta equivocada no falla: devuelve Nothing y el renglón
    ''' sale «(absent)» sobre un record que SÍ trae las banderas. Ya había escrito las dos mal. Con
    ''' (firma, campo) el contenedor no participa, que es justamente lo que <c>WbEdit.FindField</c>
    ''' existe para resolver.</para></summary>
    Public Shared Function DescribirBanderas(nodo As Canon.WbNode, firma As String, campo As String) As String
        If nodo Is Nothing Then Return "(absent)"
        Dim n = Canon.WbEdit.FindField(nodo, firma, campo)
        If n Is Nothing OrElse n.Value Is Nothing Then Return "(absent)"
        Dim def = TryCast(n.Def, Canon.WbIntegerDef)
        Return NombresDeBits(Canon.CanonBridge.AEntero(n.Value), If(def Is Nothing, Nothing, def.FlagNames))
    End Function

    ''' <summary>Los bits prendidos de <paramref name="valor"/> con el nombre que les da
    ''' <paramref name="nombres"/> (indexado por número de bit).
    ''' <para>Un bit prendido SIN nombre declarado sale como <c>unk bit N</c> y no se descarta: un bit
    ''' desconocido es información, y esconderlo haría que el panel describa mal el record.</para></summary>
    Public Shared Function NombresDeBits(valor As Long, nombres As String()) As String
        If valor = 0L Then Return "(none)"
        Dim partes As New List(Of String)
        For bit = 0 To 63
            If (valor And (1L << bit)) = 0L Then Continue For
            Dim nm As String = Nothing
            If nombres IsNot Nothing AndAlso bit < nombres.Length Then nm = nombres(bit)
            partes.Add(If(String.IsNullOrEmpty(nm), $"unk bit {bit}", nm))
        Next
        Return String.Join(", ", partes)
    End Function

    ''' <summary>ACBS +6 (FO4) / +8 (SSE) is a union: a fixed Level, or — when the PC Level Mult
    ''' flag (0x80) is set — a multiplier stored ×1000. Reading it as a flat level for a
    ''' PC-levelled actor shows "Level: 1000" instead of "1.00x".</summary>
    Public Shared Function FormatAcbsLevel(npc As Canon.INpc) As String
        If npc Is Nothing Then Return "(none)"
        Dim nivel = npc.NivelDeConfiguracion()
        Dim rango = $"(calc {npc.ConfigurationCalcMinLevel}..{npc.ConfigurationCalcMaxLevel})"
        If npc.ConfigurationFlagsPCLevelMult Then Return $"PC Level Mult: {nivel / 1000.0F:F2}x  {rango}"
        Return $"Level: {nivel}  {rango}"
    End Function

    Public Shared Function FormatSlotMask(mask As UInteger) As String
        If mask = 0UI Then Return "(none)"
        Dim slots As New List(Of String)
        Dim bitMask As UInteger = 1UI
        For bit = 0 To 31
            If (mask And bitMask) <> 0UI Then
                slots.Add((30 + bit).ToString())
            End If
            bitMask <<= 1
        Next
        Return String.Join(",", slots)
    End Function
End Class
