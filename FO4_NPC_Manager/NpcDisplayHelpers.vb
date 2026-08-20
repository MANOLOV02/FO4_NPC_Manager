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

''' <summary>NPC display / search / filter helpers. Extracted from MainForm (pure stateless, no
''' instance state, no UI). Real separate class (NOT a partial). See 61-perf-mainform-split.</summary>
Friend NotInheritable Class NpcDisplayHelpers
    Private Sub New()
    End Sub

    ''' <summary>Build the concatenated lowercase searchable text for an NPC. Mirror the same 6
    ''' fields que MatchesNpcFilter comparaba (ToString, EditorID, FullName, PluginName,
    ''' FormID hex). Single string permite reducir el match a un IndexOf en lugar de 6.</summary>
    Public Shared Function BuildNpcSearchableText(npc As NPC_Data) As String
        If npc Is Nothing Then Return ""
        Dim sb As New System.Text.StringBuilder()
        sb.Append(If(npc.ToString(), "")).Append("|"c)
        sb.Append(If(npc.EditorID, "")).Append("|"c)
        sb.Append(If(npc.Record.Name, "")).Append("|"c)
        sb.Append(If(npc.PluginName, "")).Append("|"c)
        sb.Append(npc.FormID.ToString("X8"))
        Return sb.ToString().ToLowerInvariant()
    End Function

    ''' <summary>Display label for an NPC tree node: "FullName (EditorID, FormID)" with fallbacks
    ''' a EditorID (FormID) cuando no hay FullName, o sólo FormID cuando tampoco hay EditorID.
    ''' Compartido por Section 1 placed NPCs y Section 2 LVLN children.</summary>
    Public Shared Function BuildNpcDisplayLabel(npc As NPC_Data) As String
        Dim formIdText = npc.FormID.ToString("X8")
        If npc.Record.Name <> "" Then
            Dim parenContent = If(npc.EditorID <> "", $"{npc.EditorID}, {formIdText}", formIdText)
            Return $"{npc.Record.Name} ({parenContent})"
        ElseIf npc.EditorID <> "" Then
            Return $"{npc.EditorID} ({formIdText})"
        End If
        Return formIdText
    End Function

    Public Shared Function GetNpcNodeDisplayText(npc As NPC_Data, dependencyEdge As MainForm.TemplateDependencyEdge) As String
        Dim baseText = If(npc Is Nothing, "<unknown NPC>", npc.ToString())
        If dependencyEdge Is Nothing OrElse dependencyEdge.Categories.Count = 0 Then Return baseText
        Return $"{baseText} (template: {String.Join(", ", dependencyEdge.Categories)})"
    End Function

    Public Shared Function MatchesRecordFilter(rec As PluginRecord, filter As String) As Boolean
        If String.IsNullOrWhiteSpace(filter) Then Return True
        If rec Is Nothing Then Return False
        If Not String.IsNullOrEmpty(rec.EditorID) AndAlso rec.EditorID.Contains(filter, StringComparison.OrdinalIgnoreCase) Then Return True
        If rec.Header.FormID.ToString("X8").Contains(filter, StringComparison.OrdinalIgnoreCase) Then Return True
        If Not String.IsNullOrEmpty(rec.SourcePluginName) AndAlso rec.SourcePluginName.Contains(filter, StringComparison.OrdinalIgnoreCase) Then Return True
        If Not String.IsNullOrEmpty(rec.Header.Signature) AndAlso rec.Header.Signature.Contains(filter, StringComparison.OrdinalIgnoreCase) Then Return True
        Return False
    End Function

End Class
