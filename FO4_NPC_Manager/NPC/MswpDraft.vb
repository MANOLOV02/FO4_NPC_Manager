Imports FO4_Base_Library

''' <summary>A Material Swap (MSWP) record being authored in the (future) ARMA/ARMO/MSWP editor — an
''' in-memory draft owned by MainForm (process scope) until persisted via the Save dialog. Mirrors
''' <see cref="OutfitDraft"/> exactly (same provisional-FormID/dirty/EditorID scheme, shared draft
''' FormID counter) but for the MSWP record type. This draft IS the authoring model the writer's
''' <see cref="SaveNpcEspWriter.MswpRecordEntry"/> is built from in the saver (mirror of how an
''' <see cref="OutfitDraft"/> becomes an <c>OtftRecordEntry</c> in Phase 2c) — so its fields mirror
''' that entry class field-for-field.
'''
''' Two flavours (same contract as <see cref="OutfitDraft"/>):
'''   • NEW (IsOverride=False): a brand-new MSWP. <see cref="FormID"/> is a PROVISIONAL sentinel
'''     (high byte 0xFF, <see cref="OutfitDraft.IsDraftFormID"/>) so other drafts (ARMA/ARMO material
'''     swaps) can reference it before save; the writer assigns the real plugin self-index FormID and
'''     remaps every reference at save time.
'''   • OVERRIDE (IsOverride=True): an edit of an existing MSWP keeping its EditorID. <see cref="FormID"/>
'''     IS that record's real GLOBAL FormID from the load order; the saver fetches
'''     <c>PluginManager.GetRecord(FormID)</c> for the entry's <c>SourceRecord</c> and reads the original
'''     VCS from its header.</summary>
Public Class MswpDraft

    ''' <summary>Working EditorID prefix (type segment): <c>npcm_MSWP_&lt;name&gt;</c>. At save the
    ''' destination plugin name is injected (NpcOverrideSaver.ApplyEspNamespaceToEditorId) → final
    ''' <c>npcm_&lt;ESPNAME&gt;_MSWP_&lt;name&gt;</c>, identifiable + per-plugin namespaced in xEdit.</summary>
    Public Const EditorIdPrefix As String = "npcm_MSWP_"

    ''' <summary>NEW: provisional sentinel (0xFF…, from MainForm.AllocateDraftFormID). OVERRIDE: the
    ''' existing MSWP's real GLOBAL FormID (the saver uses it both as the entry FormID and as the
    ''' <c>GetRecord</c> key for <c>SourceRecord</c>).</summary>
    Public Property FormID As UInteger
    Public Property EditorID As String = ""

    ''' <summary>FNAM 'Tree Folder' (ZSTRING). Optional — emitted only when non-empty.</summary>
    Public Property TreeFolder As String = ""

    ''' <summary>The substitution pairs (BNAM original / SNAM replacement / optional CNAM color remap
    ''' index). Reuses the lib data class so the saver maps it 1:1 into the writer entry.</summary>
    Public ReadOnly Property Substitutions As New List(Of MSWP_Substitution)

    ''' <summary>True = override an existing MSWP (keep its EditorID + FormID). False = brand-new MSWP.</summary>
    Public Property IsOverride As Boolean
    ''' <summary>Never written to the ESP yet.</summary>
    Public Property IsNew As Boolean = True
    ''' <summary>Written before, edited again since.</summary>
    Public Property IsModified As Boolean = False

    ''' <summary>Either flag set → the save must (re)write it. Both cleared after save.</summary>
    Public ReadOnly Property IsDirty As Boolean
        Get
            Return IsNew OrElse IsModified
        End Get
    End Property

    Public Function Clone() As MswpDraft
        Dim c As New MswpDraft With {
            .FormID = FormID,
            .EditorID = EditorID,
            .TreeFolder = TreeFolder,
            .IsOverride = IsOverride,
            .IsNew = IsNew,
            .IsModified = IsModified
        }
        For Each s In Substitutions
            c.Substitutions.Add(New MSWP_Substitution With {
                .OriginalMaterial = s.OriginalMaterial,
                .ReplacementMaterial = s.ReplacementMaterial,
                .TreeFolder = s.TreeFolder,
                .HasColorRemapIndex = s.HasColorRemapIndex,
                .ColorRemapIndex = s.ColorRemapIndex
            })
        Next
        Return c
    End Function

End Class
