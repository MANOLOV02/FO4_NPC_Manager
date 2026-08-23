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
        Dim ins = If(c.IsAdditive, "⊕ ", "")
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

    ''' <summary>ACBS Flags (u32). Bits 0x01..0x80, 0x800, 0x4000, 0x10000, 0x40000..0x100000,
    ''' 0x20000000, 0x80000000 carry the same meaning in both games; four bits do not, so the
    ''' game decides. Unnamed set bits are reported as unk0xN rather than dropped — an unknown
    ''' bit is information, and silently hiding it would misreport the record.</summary>
    Public Shared Function DescribeAcbsFlags(flags As UInteger, game As Config_App.Game_Enum) As String
        If flags = 0UI Then Return "(none)"
        Dim isSse = (game = Config_App.Game_Enum.Skyrim)
        Dim names As New Dictionary(Of UInteger, String) From {
            {&H1UI, "Female"},
            {&H2UI, "Essential"},
            {&H4UI, "Is CharGen Face Preset"},
            {&H8UI, "Respawn"},
            {&H10UI, "Auto-calc stats"},
            {&H20UI, "Unique"},
            {&H40UI, "Doesn't affect stealth meter"},
            {&H80UI, "PC Level Mult"},
            {&H800UI, "Protected"},
            {&H4000UI, "Summonable"},
            {&H10000UI, "Doesn't bleed"},
            {&H40000UI, "Bleedout Override"},
            {&H80000UI, "Opposite Gender Anims"},
            {&H100000UI, "Simple Actor"},
            {&H20000000UI, "Is Ghost"},
            {&H80000000UI, "Invulnerable"}
        }
        If isSse Then
            names.Add(&H100UI, "Use Template?")
            names.Add(&H200000UI, "looped script?")
            names.Add(&H10000000UI, "looped audio?")
        Else
            names.Add(&H200UI, "Calc For Each Template")
            names.Add(&H800000UI, "No Activation/Hellos")
            names.Add(&H1000000UI, "Diffuse Alpha Test")
        End If

        Dim parts As New List(Of String)
        For bit = 0 To 31
            Dim mask As UInteger = 1UI << bit
            If (flags And mask) = 0UI Then Continue For
            Dim nm As String = Nothing
            parts.Add(If(names.TryGetValue(mask, nm), nm, $"unk0x{mask:X}"))
        Next
        Return String.Join(", ", parts)
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

    Private Shared Function EnumName(names As String(), value As Integer) As String
        If value >= 0 AndAlso value < names.Length Then Return $"{names(value)} ({value})"
        Return value.ToString()
    End Function

    Public Shared Function AggressionName(v As Byte) As String
        Return EnumName({"Unaggressive", "Aggressive", "Very Aggressive", "Frenzied"}, v)
    End Function

    Public Shared Function ConfidenceName(v As Byte) As String
        Return EnumName({"Cowardly", "Cautious", "Average", "Brave", "Foolhardy"}, v)
    End Function

    Public Shared Function MoralityName(v As Byte) As String
        Return EnumName({"Any Crime", "Violence Against Enemies", "Property Crime Only", "No Crime"}, v)
    End Function

    Public Shared Function AssistanceName(v As Byte) As String
        Return EnumName({"Helps Nobody", "Helps Allies", "Helps Friends and Allies"}, v)
    End Function

    Public Shared Function MoodName(v As Byte) As String
        Return EnumName({"Neutral", "Angry", "Fear", "Happy", "Sad", "Surprised", "Puzzled", "Disgusted"}, v)
    End Function

    ''' <summary>NAM8 Sound Level. FO4 appends a 5th value ('Quiet') that Skyrim does not have.</summary>
    Public Shared Function SoundLevelName(v As UInteger, game As Config_App.Game_Enum) As String
        Dim names As String()
        If game = Config_App.Game_Enum.Skyrim Then
            names = {"Loud", "Normal", "Silent", "Very Loud"}
        Else
            names = {"Loud", "Normal", "Silent", "Very Loud", "Quiet"}
        End If
        Return EnumName(names, CInt(v))
    End Function

    ''' <summary>NPC_.NAM9 slider order — SSE only. This IS the byte layout (slider i = float at +4i),
    ''' so the order is schema, not presentation. The "Farward" spellings are preserved as-is
    ''' (not a typo in this code).</summary>
    Public Shared ReadOnly SseFaceMorphSliderNames As String() = {
        "Nose Long/Short", "Nose Up/Down", "Jaw Up/Down", "Jaw Narrow/Wide", "Jaw Farward/Back",
        "Cheeks Up/Down", "Cheeks Farward/Back", "Eyes Up/Down", "Eyes In/Out", "Brows Up/Down",
        "Brows In/Out", "Brows Farward/Back", "Lips Up/Down", "Lips In/Out", "Chin Narrow/Wide",
        "Chin Up/Down", "Chin Underbite/Overbite", "Eyes Farward/Back", "VampireMorph"}

    ''' <summary>NPC_.NAMA fields — SSE only. Index 1 is unnamed in the schema.</summary>
    Public Shared ReadOnly SseFacePartNames As String() = {"Nose", "Unknown", "Eyes", "Mouth"}

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
