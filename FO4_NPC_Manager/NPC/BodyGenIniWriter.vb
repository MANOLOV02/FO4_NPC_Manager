Imports System.IO
Imports System.Text

''' <summary>Emits the F4SE/LooksMenu BodyGen .ini pair (<c>templates.ini</c> +
''' <c>morphs.ini</c>) so the engine applies BodySlide sliders to NPCs on first-load. Format
''' verified against <c>Script extenders, Racemenu y Looksmenu/F4SEPlugins/f4ee/BodyGenInterface.cpp:21-158</c>
''' (<c>ReadBodyMorphTemplates</c>) and <c>:185-426</c> (<c>ReadBodyMorphs</c>).
'''
''' <para>Convention chosen for NPC_Manager (single owner of these files): one template per NPC
''' named <c>NPCM_&lt;EditorID&gt;</c> (sanitized), one row in <c>morphs.ini</c> per NPC. Values
''' are fixed (no random ranges, no alternative sets). The directory is anchored to the
''' OVERRIDE plugin's filename (not the NPC's master plugin) WITH its extension (e.g.
''' <c>NPC_Manager.esp</c>): the engine does a targeted per-mod lookup <c>BodyGen\&lt;modInfo-&gt;name&gt;\</c>
''' (BodyGenInterface.cpp:534; name resolved via GetLoadedModIndex, which expects the extension) — it does
''' NOT enumerate subdirs. Ownership by the override plugin lets a user remove the override + .ini cleanly.</para>
'''
''' <para>Caveat (must be surfaced in UI): the engine evaluates BodyGen only on the actor's
''' first load. NPCs already spawned in a saved game keep whatever BodyMorphs were persisted to
''' the F4SE co-save when they spawned originally — these .ini files do not apply
''' retroactively. New saves / fresh actors pick them up.</para></summary>
Public Module BodyGenIniWriter

    ''' <summary>One emitted row, paired with its template definition. <see cref="BodyMorphs"/>
    ''' is the slider→value dict captured from the NPC's overlay; entries with an empty dict
    ''' are dropped by <see cref="Emit"/> (a template with no morphs is a no-op).</summary>
    Public Class NpcEntry
        ''' <summary>Template name as it will appear in both files. Caller is expected to pass
        ''' the sanitized form (see <see cref="SanitizeTemplateName"/>); Emit re-runs sanitize
        ''' as a safety net to guarantee the .ini stays parseable.</summary>
        Public TemplateName As String = ""
        ''' <summary>Filename of the plugin that originally defines the NPC (e.g.
        ''' <c>Fallout4.esm</c>). The engine matches morphs.ini rows by master plugin, so this
        ''' must be the source master — not the override plugin we are saving to.</summary>
        Public MasterPluginFileName As String = ""
        ''' <summary>OBJECT ID del dueño en hex de 6 dígitos: 12 bits útiles si el master es light, 24 si es
        ''' completo (PluginManager.ToFaceGenLocalFormID). NO es "el local de 24 bits": para un master ESL eso
        ''' arrastraría el light slot de la sesión que lo escribió, y f4ee ORea 24 bits CRUDOS sin enmascarar
        ''' (BodyGenInterface.cpp:319-321), con lo que el slot viejo se mezcla con el actual y da uno tercero.
        ''' El valor llega ya canónico porque BssliderSidecar.NormalizeKeys normaliza la clave del sidecar.</summary>
        Public LocalFormIDHex As String = ""
        ''' <summary>Gender filter: <c>"male"</c>, <c>"female"</c>, or empty (apply to both).
        ''' Matches the third pipe-separated token <c>BodyGenInterface.cpp:185+</c> accepts.</summary>
        Public Gender As String = ""
        Public BodyMorphs As Dictionary(Of String, Single)
    End Class

    ''' <summary>True when a BodyGen .ini pair already exists on disk for <paramref name="targetPluginBaseName"/>
    ''' (templates.ini or morphs.ini under <c>Data\F4SE\Plugins\F4EE\BodyGen\&lt;base&gt;\</c>). Lets the
    ''' mark-to-delete flow re-emit (to DROP a removed NPC) only when there is an .ini to update — never
    ''' creating one the user didn't ask for.</summary>
    Public Function IniExists(dataPath As String, targetPluginBaseName As String) As Boolean
        If String.IsNullOrEmpty(dataPath) OrElse String.IsNullOrEmpty(targetPluginBaseName) Then Return False
        Dim bodyGenDir = Path.Combine(dataPath, "F4SE", "Plugins", "F4EE", "BodyGen", targetPluginBaseName)
        Return File.Exists(Path.Combine(bodyGenDir, "templates.ini")) OrElse File.Exists(Path.Combine(bodyGenDir, "morphs.ini"))
    End Function

    Public Sub Emit(dataPath As String, targetPluginBaseName As String, entries As List(Of NpcEntry))
        If String.IsNullOrEmpty(dataPath) OrElse String.IsNullOrEmpty(targetPluginBaseName) Then Return

        Dim bodyGenDir = Path.Combine(dataPath, "F4SE", "Plugins", "F4EE", "BodyGen", targetPluginBaseName)
        Dim templatesPath = Path.Combine(bodyGenDir, "templates.ini")
        Dim morphsPath = Path.Combine(bodyGenDir, "morphs.ini")

        ' Drop entries that have nothing to apply. A template with zero morphs would parse but
        ' do nothing at runtime; leave it out so the .ini stays small and diff-friendly.
        Dim usable = If(entries, New List(Of NpcEntry)()).
            Where(Function(e) e IsNot Nothing AndAlso
                              e.BodyMorphs IsNot Nothing AndAlso
                              e.BodyMorphs.Count > 0 AndAlso
                              Not String.IsNullOrEmpty(e.MasterPluginFileName) AndAlso
                              Not String.IsNullOrEmpty(e.LocalFormIDHex)).
            ToList()

        If usable.Count = 0 Then
            ' Nothing to persist — wipe prior files so stale templates don't apply to NPCs
            ' the user has since cleared. Directory itself stays (cheap, doesn't matter).
            TryDeleteFile(templatesPath)
            TryDeleteFile(morphsPath)
            Return
        End If

        ' Stable ordering: templates by name, morphs.ini grouped by master then formid. Keeps
        ' diffs across saves readable and avoids dict-iteration-order noise.
        Dim orderedTemplates = usable.OrderBy(Function(e) SanitizeTemplateName(e.TemplateName), StringComparer.Ordinal).ToList()
        Dim orderedMorphs = usable.
            OrderBy(Function(e) e.MasterPluginFileName, StringComparer.OrdinalIgnoreCase).
            ThenBy(Function(e) e.LocalFormIDHex, StringComparer.Ordinal).
            ToList()

        Directory.CreateDirectory(bodyGenDir)

        ' --- templates.ini
        Dim sbTemplates As New StringBuilder()
        sbTemplates.AppendLine("# Auto-generated by NPC_Manager. Do not edit by hand —")
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
        sbMorphs.AppendLine("# Auto-generated by NPC_Manager. Do not edit by hand —")
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
    ''' other than ASCII letters/digits/underscore is replaced with underscore.
    ''' <c>BodyGenInterface.cpp</c>'s parser splits on whitespace + <c>=</c> + <c>,</c> +
    ''' <c>/</c> + <c>|</c>, so unrestricted EditorIDs (e.g. <c>"Companion Cait"</c>) would
    ''' break template lookup silently. Always returned non-empty: an entirely-non-ASCII
    ''' input falls back to <c>"Unnamed"</c>.</summary>
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
    ''' <c>"name1@value1, name2@value2"</c>. Values use invariant-culture float formatting
    ''' so the ini parser (which uses <c>strtof</c>-equivalent) reads them back identically.
    ''' Sliders are emitted in ordinal order for diff stability.</summary>
    Private Function BuildMorphSpecList(morphs As Dictionary(Of String, Single)) As String
        Dim parts = morphs.
            OrderBy(Function(kv) kv.Key, StringComparer.Ordinal).
            Select(Function(kv) $"{kv.Key}@{kv.Value.ToString(Globalization.CultureInfo.InvariantCulture)}")
        Return String.Join(", ", parts)
    End Function

    Private Sub WriteAtomic(path As String, content As String)
        Dim tmp = path & ".tmp"
        File.WriteAllText(tmp, content, New UTF8Encoding(encoderShouldEmitUTF8Identifier:=False))
        ' Misma ley atómica que SaveNpcEspWriter y LoadOrderActivator: Delete+Move deja una ventana en la que
        ' el archivo no existe. File.Replace exige que el destino exista; el Move queda para el caso nuevo.
        If File.Exists(path) Then
            File.Replace(tmp, path, Nothing, ignoreMetadataErrors:=True)
        Else
            File.Move(tmp, path)
        End If
    End Sub

    Private Sub TryDeleteFile(path As String)
        Try
            If File.Exists(path) Then File.Delete(path)
        Catch
            ' Best-effort; the file may be locked or read-only. A stale file left here only
            ' hurts the next save, which will try the delete again.
        End Try
    End Sub

End Module
