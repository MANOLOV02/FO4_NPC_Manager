Imports System.IO
Imports System.Text

''' <summary>Emits the SKEE64 (RaceMenu) BodyGen .ini pair (<c>templates.ini</c> +
''' <c>morphs.ini</c>) for Skyrim SE so the engine applies BodySlide sliders to NPCs on
''' first-load. This is the SSE counterpart of <see cref="BodyGenIniWriter"/> — same DSL, but a
''' DIFFERENT output directory.
'''
''' <para>Output dir (verified against skee64 <c>BodyMorphInterface.h:47</c> dir
''' <c>actors\character\BodyGenData\</c> + <c>.cpp:128,132,138</c> which prepend <c>Meshes\</c>,
''' read through <c>BSResourceNiBinaryStream</c> so loose files are honored):
''' <c>Data\Meshes\actors\character\BodyGenData\&lt;OverridePluginBaseName&gt;\</c>. The FO4 writer
''' uses <c>Data\F4SE\Plugins\F4EE\BodyGen\&lt;plugin&gt;\</c> — do NOT confuse the two.</para>
'''
''' <para>Convention (single owner of these files, mirrors the FO4 writer): one template per NPC
''' named <c>NPCM_&lt;EditorID&gt;</c> (sanitized), one row in <c>morphs.ini</c> per NPC. Values are
''' fixed (no random ranges, no alternative sets). The directory is anchored to the OVERRIDE
''' plugin's basename so removing the override + .ini together stays clean.</para>
'''
''' <para>Caveat (surface in UI): BodyGen is evaluated only on the actor's first in-game load;
''' these .ini files do not apply retroactively to actors already spawned in a save.</para></summary>
Public Module SseBodyGenIniWriter

    ''' <summary>One emitted row, paired with its template definition. Same shape as
    ''' <see cref="BodyGenIniWriter.NpcEntry"/> (SSE writer keeps its own copy rather than sharing,
    ''' per plan §5B). <see cref="BodyMorphs"/> is the flat (summed) slider→value dict; entries with
    ''' an empty dict are dropped by <see cref="Emit"/>.</summary>
    Public Class NpcEntry
        ''' <summary>Template name as it will appear in both files (sanitized form expected; Emit
        ''' re-runs sanitize as a safety net).</summary>
        Public TemplateName As String = ""
        ''' <summary>Filename of the plugin that originally defines the NPC (e.g.
        ''' <c>Skyrim.esm</c>). The engine matches morphs.ini rows by master plugin — this must be
        ''' the source master, not the override plugin we are saving to.</summary>
        Public MasterPluginFileName As String = ""
        ''' <summary>NPC's local 24-bit FormID as 6-digit uppercase hex.</summary>
        Public LocalFormIDHex As String = ""
        ''' <summary>Gender filter: <c>"male"</c>, <c>"female"</c>, or empty (apply to both). Matches
        ''' the optional third pipe-separated token of the <c>ModName|FormIDHex[|Gender][|Race]</c>
        ''' LHS grammar.</summary>
        Public Gender As String = ""
        Public BodyMorphs As Dictionary(Of String, Single)
    End Class

    ''' <summary>True when an SSE BodyGen .ini pair already exists on disk for
    ''' <paramref name="targetPluginBaseName"/> (templates.ini or morphs.ini under
    ''' <c>Data\Meshes\actors\character\BodyGenData\&lt;base&gt;\</c>). Lets a delete-only re-emit
    ''' update an existing pair without ever creating one the user didn't ask for.</summary>
    Public Function IniExists(dataPath As String, targetPluginBaseName As String) As Boolean
        If String.IsNullOrEmpty(dataPath) OrElse String.IsNullOrEmpty(targetPluginBaseName) Then Return False
        Dim bodyGenDir = Path.Combine(dataPath, "Meshes", "actors", "character", "BodyGenData", targetPluginBaseName)
        Return File.Exists(Path.Combine(bodyGenDir, "templates.ini")) OrElse File.Exists(Path.Combine(bodyGenDir, "morphs.ini"))
    End Function

    ''' <summary>Emit <c>Data\Meshes\actors\character\BodyGenData\&lt;targetPluginBaseName&gt;\</c>
    ''' with <c>templates.ini</c> + <c>morphs.ini</c>. <paramref name="targetPluginBaseName"/> is the
    ''' override plugin filename WITHOUT extension. Merge-safe like the FO4 writer: the caller passes
    ''' the FULL entry list (built from the whole sidecar), so a full rewrite preserves every other
    ''' NPC's row and replaces this NPC's. If the filtered list is empty BOTH files are deleted.
    ''' Atomic writes via .tmp + rename.</summary>
    Public Sub Emit(dataPath As String, targetPluginBaseName As String, entries As List(Of NpcEntry))
        If String.IsNullOrEmpty(dataPath) OrElse String.IsNullOrEmpty(targetPluginBaseName) Then Return

        Dim bodyGenDir = Path.Combine(dataPath, "Meshes", "actors", "character", "BodyGenData", targetPluginBaseName)
        Dim templatesPath = Path.Combine(bodyGenDir, "templates.ini")
        Dim morphsPath = Path.Combine(bodyGenDir, "morphs.ini")

        ' Drop entries with nothing to apply — a template with zero morphs would parse but be a no-op.
        Dim usable = If(entries, New List(Of NpcEntry)()).
            Where(Function(e) e IsNot Nothing AndAlso
                              e.BodyMorphs IsNot Nothing AndAlso
                              e.BodyMorphs.Count > 0 AndAlso
                              Not String.IsNullOrEmpty(e.MasterPluginFileName) AndAlso
                              Not String.IsNullOrEmpty(e.LocalFormIDHex)).
            ToList()

        If usable.Count = 0 Then
            ' Nothing to persist — wipe prior files so stale templates don't apply to NPCs the user
            ' has since cleared. Directory itself stays.
            TryDeleteFile(templatesPath)
            TryDeleteFile(morphsPath)
            Return
        End If

        ' Stable ordering: templates by name, morphs grouped by master then formid — diff-friendly.
        Dim orderedTemplates = usable.OrderBy(Function(e) SanitizeTemplateName(e.TemplateName), StringComparer.Ordinal).ToList()
        Dim orderedMorphs = usable.
            OrderBy(Function(e) e.MasterPluginFileName, StringComparer.OrdinalIgnoreCase).
            ThenBy(Function(e) e.LocalFormIDHex, StringComparer.Ordinal).
            ToList()

        Directory.CreateDirectory(bodyGenDir)

        ' --- templates.ini
        Dim sbTemplates As New StringBuilder()
        sbTemplates.AppendLine("# Auto-generated by NPC_Manager (Skyrim SE). Do not edit by hand —")
        sbTemplates.AppendLine("# regenerated each time you Save ESP with the BodyGen checkbox on.")
        sbTemplates.AppendLine()
        For Each e In orderedTemplates
            sbTemplates.Append(SanitizeTemplateName(e.TemplateName))
            sbTemplates.Append(" = ")
            sbTemplates.AppendLine(BuildMorphSpecList(e.BodyMorphs))
        Next
        WriteAtomic(templatesPath, sbTemplates.ToString())

        ' --- morphs.ini
        Dim sbMorphs As New StringBuilder()
        sbMorphs.AppendLine("# Auto-generated by NPC_Manager (Skyrim SE). Do not edit by hand —")
        sbMorphs.AppendLine("# regenerated each time you Save ESP with the BodyGen checkbox on.")
        sbMorphs.AppendLine()
        For Each e In orderedMorphs
            sbMorphs.Append(e.MasterPluginFileName)
            sbMorphs.Append("|"c)
            sbMorphs.Append(e.LocalFormIDHex)
            If Not String.IsNullOrEmpty(e.Gender) Then
                sbMorphs.Append("|"c)
                sbMorphs.Append(e.Gender)
            End If
            sbMorphs.Append(" = ")
            sbMorphs.AppendLine(SanitizeTemplateName(e.TemplateName))
        Next
        WriteAtomic(morphsPath, sbMorphs.ToString())
    End Sub

    ''' <summary>Sanitize a string into a BodyGen-safe identifier. Whitespace and any character
    ''' other than ASCII letters/digits/underscore is replaced with underscore (the parser splits on
    ''' whitespace + <c>=</c> + <c>,</c> + <c>/</c> + <c>|</c>). Always non-empty; an entirely
    ''' non-ASCII input falls back to <c>"Unnamed"</c>. Copied from <see cref="BodyGenIniWriter"/>
    ''' (per plan §5B: copy, don't share the Private helpers).</summary>
    Public Function SanitizeTemplateName(name As String) As String
        If String.IsNullOrEmpty(name) Then Return "Unnamed"
        Dim sb As New StringBuilder(name.Length)
        For Each ch In name
            If (ch >= "a"c AndAlso ch <= "z"c) OrElse
               (ch >= "A"c AndAlso ch <= "Z"c) OrElse
               (ch >= "0"c AndAlso ch <= "9"c) OrElse
               ch = "_"c Then
                sb.Append(ch)
            Else
                sb.Append("_"c)
            End If
        Next
        Dim result = sb.ToString()
        If String.IsNullOrEmpty(result) Then Return "Unnamed"
        Return result
    End Function

    ''' <summary>Format a slider-dict as the morph-spec list expected by BodyGen templates:
    ''' <c>"name1@value1, name2@value2"</c>, invariant-culture floats, ordinal order for diff
    ''' stability. Copied from <see cref="BodyGenIniWriter"/>.</summary>
    Private Function BuildMorphSpecList(morphs As Dictionary(Of String, Single)) As String
        Dim parts = morphs.
            OrderBy(Function(kv) kv.Key, StringComparer.Ordinal).
            Select(Function(kv) $"{kv.Key}@{kv.Value.ToString(Globalization.CultureInfo.InvariantCulture)}")
        Return String.Join(", ", parts)
    End Function

    Private Sub WriteAtomic(path As String, content As String)
        Dim tmp = path & ".tmp"
        File.WriteAllText(tmp, content, New UTF8Encoding(encoderShouldEmitUTF8Identifier:=False))
        If File.Exists(path) Then File.Delete(path)
        File.Move(tmp, path)
    End Sub

    Private Sub TryDeleteFile(path As String)
        Try
            If File.Exists(path) Then File.Delete(path)
        Catch
            ' Best-effort; a stale file left here only hurts the next save, which retries the delete.
        End Try
    End Sub

End Module
