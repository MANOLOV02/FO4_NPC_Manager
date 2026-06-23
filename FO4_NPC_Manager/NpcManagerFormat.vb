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

''' <summary>Pure stateless formatting / label helpers extracted from MainForm (no instance state,
''' no UI, no MainForm fields). Real separate class — NOT a partial of MainForm. Part of slimming
''' MainForm.vb; see project_mainform_split. Call sites use the qualified <c>NpcManagerFormat.X</c>.</summary>
Friend NotInheritable Class NpcManagerFormat
    Private Sub New()
    End Sub

    Public Shared Function DescribeNpc(npc As NPC_Data) As String
        If npc Is Nothing Then Return "<unknown NPC>"
        If npc.EditorID <> "" Then Return npc.EditorID
        If npc.FullName <> "" Then Return npc.FullName
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

    Public Shared Function AnimClipLabel(c As ResolvedAnimationClip) As String
        Dim nm = If(String.IsNullOrWhiteSpace(c.ClipName), System.IO.Path.GetFileNameWithoutExtension(c.AnimationFile), c.ClipName)
        Dim roles = If(c.Roles.Count > 0, $"  [{String.Join(",", c.Roles)}]", "")
        Dim fp = If(c.Is1stPersonOnly, "  · 1st-person", "")
        Return $"{nm}{roles}{fp}"
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
