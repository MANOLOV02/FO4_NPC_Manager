Imports System.Windows.Forms

''' <summary>Shared, UNIFORM EditorID-field model for the four draft editors (ARMA / ARMO / MSWP / Outfit) so
''' they all present the EditorID the same, clear way (chosen by the user 2026-07-04 over the old ad-hoc styles).
'''
''' Model: a NEW draft edits ONLY the &lt;name&gt; portion. The fixed type prefix (e.g. <c>npcm_ARMA_</c>) is shown
''' read-only in a prefix Label, and the destination plugin's namespace segment — injected at Save by
''' <see cref="FO4_NPC_Manager.NpcOverrideSaver.ApplyEspNamespaceToEditorId"/> and therefore unknown while editing —
''' is shown as the <see cref="PluginPlaceholder"/> token in a live "Saves as:" preview Label. So the user always
''' sees the exact final shape <c>npcm_&lt;plugin&gt;_ARMA_&lt;name&gt;</c> without being able to break the fixed parts.
''' An OVERRIDE draft keeps its target record's EditorID verbatim (Save does NOT namespace overrides), so the field
''' is shown read-only as "EditorID (kept): …".
'''
''' The STORED draft EditorID is the pre-namespace base "&lt;typeprefix&gt;&lt;name&gt;" (e.g. <c>npcm_ARMA_myArmor</c>);
''' Save injects the plugin segment. Each editor declares three controls (prefix <see cref="Label"/>, name
''' <see cref="TextBox"/>, preview <see cref="Label"/>) and drives them through these helpers — the "container in the
''' Designer, behavior in a shared module" pattern (mirror of <see cref="BipedSlotCheckboxes"/>).</summary>
Public Module EditorIdField

    Private Const Npcm As String = "npcm_"

    ''' <summary>The token standing in for the target plugin's namespace segment (resolved at Save).</summary>
    Public Const PluginPlaceholder As String = "<plugin>"

    ''' <summary>Fixed prefix shown for a NEW draft: e.g. "npcm_&lt;plugin&gt;_ARMA_" from the type prefix
    ''' "npcm_ARMA_". Non-<c>npcm_</c> prefixes (shouldn't happen) pass through unchanged.</summary>
    Public Function DisplayPrefix(typePrefix As String) As String
        If String.IsNullOrEmpty(typePrefix) OrElse Not typePrefix.StartsWith(Npcm, StringComparison.Ordinal) Then Return typePrefix
        Return Npcm & PluginPlaceholder & "_" & typePrefix.Substring(Npcm.Length)
    End Function

    ''' <summary>The editable &lt;name&gt; part of a stored base EditorID (strips the type prefix; tolerant of an
    ''' EDID that doesn't carry the prefix → returned unchanged).</summary>
    Public Function NameFromEditorId(baseEdid As String, typePrefix As String) As String
        Dim s = If(baseEdid, "")
        If s.StartsWith(typePrefix, StringComparison.OrdinalIgnoreCase) Then Return s.Substring(typePrefix.Length)
        Return s
    End Function

    ''' <summary>Compose the stored base EditorID from the user's name: "&lt;typeprefix&gt;&lt;name&gt;" (trimmed).</summary>
    Public Function Compose(typePrefix As String, name As String) As String
        Return typePrefix & If(name, "").Trim()
    End Function

    ''' <summary>Live "Saves as:" preview for a NEW draft, e.g. "Saves as:  npcm_&lt;plugin&gt;_ARMA_myArmor".</summary>
    Public Function PreviewText(typePrefix As String, name As String) As String
        Return "Saves as:  " & DisplayPrefix(typePrefix) & If(name, "").Trim()
    End Function

    ''' <summary>Configure the three controls for a NEW draft: fixed prefix label, editable name box (seeded from
    ''' the base EDID's name), live preview shown. The editor wires the name box's <c>TextChanged</c> to refresh
    ''' the preview via <see cref="PreviewText"/>.</summary>
    Public Sub ConfigureNew(prefixLabel As Label, nameBox As TextBox, previewLabel As Label, typePrefix As String, baseEdid As String)
        prefixLabel.Text = DisplayPrefix(typePrefix)
        nameBox.Enabled = True
        nameBox.Text = NameFromEditorId(baseEdid, typePrefix)
        previewLabel.Visible = True
        previewLabel.Text = PreviewText(typePrefix, nameBox.Text)
    End Sub

    ''' <summary>Configure the three controls for an OVERRIDE draft: the record's EditorID is kept verbatim and
    ''' shown read-only (Save does not namespace overrides). The preview label STAYS visible with a neutral
    ''' single-line note (not hidden) so its row keeps the same height as in New mode — the layout doesn't shift
    ''' when switching New ⇄ Override.</summary>
    Public Sub ConfigureOverride(prefixLabel As Label, nameBox As TextBox, previewLabel As Label, keptEdid As String)
        prefixLabel.Text = "EditorID (kept):"
        nameBox.Enabled = False
        nameBox.Text = If(keptEdid, "")
        previewLabel.Visible = True
        previewLabel.Text = "Override — the record's EditorID is kept as-is."
    End Sub

    ''' <summary>Refresh only the live preview from the current name box text (call from the name box's
    ''' TextChanged when editing a NEW draft).</summary>
    Public Sub UpdatePreview(previewLabel As Label, typePrefix As String, name As String)
        previewLabel.Text = PreviewText(typePrefix, name)
    End Sub

End Module
