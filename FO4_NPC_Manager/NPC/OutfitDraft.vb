''' <summary>An outfit being authored in the Edit Outfit "Create" tab — a draft OTFT record that
''' lives in memory (process scope, owned by MainForm) until the user persists it via the Save
''' dialog's "Save new outfits" checkbox, at which point the writer emits it as a real OTFT in the
''' output plugin and the draft is marked clean.
'''
''' Two flavours:
'''   • NEW (IsOverride=False): a brand-new OTFT. <see cref="FormID"/> is a PROVISIONAL sentinel
'''     (high byte 0xFF, see <see cref="IsDraftFormID"/>) so the NPC can reference it before save;
'''     the writer assigns the real plugin self-index FormID and remaps the NPC.DOFT at save time.
'''   • OVERRIDE (IsOverride=True): an edit of an existing OTFT keeping its EditorID. <see cref="FormID"/>
'''     IS that record's real FormID from the load order; the writer emits an override record.
'''
''' The item list is a FLAT set of terminal ARMO FormIDs (the OTFT.INAM entries) — no LVLI, no
''' sampling. Slot conflicts among the items are resolved with <see cref="EquipResolver"/>
''' (the same engine rule the render uses), so what the user assembles renders the same way the
''' engine would equip it.</summary>
Public Class OutfitDraft

    ''' <summary>Provisional FormID high byte for unsaved NEW drafts. 0xFF is never a real plugin
    ''' master index (max 254 masters), so it can't collide with a loaded record. The writer
    ''' rewrites it to (selfMasterIndex &lt;&lt; 24 | objectIndex) on save.</summary>
    Public Const DraftFormIdHighByte As UInteger = &HFF000000UI

    ''' <summary>Working EditorID prefix (type segment) for outfits authored here: <c>npcm_Outfit_&lt;name&gt;</c>.
    ''' At save the destination plugin name is injected (NpcOverrideSaver.ApplyEspNamespaceToEditorId) →
    ''' final <c>npcm_&lt;ESPNAME&gt;_Outfit_&lt;name&gt;</c>, identifiable + per-plugin namespaced in xEdit.</summary>
    Public Const EditorIdPrefix As String = "npcm_Outfit_"

    ''' <summary>Reserved sentinel FormID for the Edit Outfit picker's throwaway "preview" draft — the
    ''' assembled Create-tab set the picker re-registers (updating its items) on every change so the
    ''' WYSIWYG render can resolve it like any draft. Object index 0x7FF sits just below the real
    ''' allocation floor (<see cref="DraftFormIdHighByte"/> | 0x800+) so it can never collide with a
    ''' committed draft. It's a draft FormID (0xFF high byte) so the render's TryGetOutfitDraft resolves
    ''' it, but it's filtered out of Browse / EDID-uniqueness / the save set — it is never persisted.</summary>
    Public Const PreviewDraftFormID As UInteger = &HFF0007FFUI

    Public Property FormID As UInteger
    Public Property EditorID As String = ""
    ''' <summary>The OTFT.INAM entries — a flat list of ARMO **or LVLI** FormIDs. ARMOs render directly;
    ''' LVLIs are sampled to a realization (see <see cref="LvliRealization"/>) and persist AS the LVLI
    ''' (the engine rolls at runtime), so the saved outfit is leveled, not flattened.</summary>
    Public ReadOnly Property ItemFormIDs As New List(Of UInteger)
    ''' <summary>Transient (not persisted, not in INAM): the currently-sampled terminal ARMO FormIDs for
    ''' each LVLI item, keyed by the LVLI FormID. Cached so the preview/conflict is STABLE between renders
    ''' (no flicker) — a Reroll clears the relevant entry to re-sample. Editor-only; the saved OTFT keeps
    ''' the LVLI reference.</summary>
    Public ReadOnly Property LvliRealization As New Dictionary(Of UInteger, List(Of UInteger))
    ''' <summary>True = override an existing OTFT (keep its EditorID + FormID). False = brand-new OTFT.</summary>
    Public Property IsOverride As Boolean
    ''' <summary>Never written to the ESP yet.</summary>
    Public Property IsNew As Boolean = True
    ''' <summary>Written before, edited again since.</summary>
    Public Property IsModified As Boolean = False

    ''' <summary>Either flag set → "Save new outfits" must (re)write it. Both cleared after save.</summary>
    Public ReadOnly Property IsDirty As Boolean
        Get
            Return IsNew OrElse IsModified
        End Get
    End Property

    ''' <summary>True if <paramref name="formID"/> is a provisional draft sentinel (0xFF high byte).
    ''' Lets the render path and resolvers detect "this DOFT points at an unsaved draft" and resolve
    ''' it from the draft's item list instead of <c>PluginManager.GetRecord</c>.</summary>
    Public Shared Function IsDraftFormID(formID As UInteger) As Boolean
        Return (formID And &HFF000000UI) = DraftFormIdHighByte
    End Function

    Public Function Clone() As OutfitDraft
        Dim c As New OutfitDraft With {
            .FormID = FormID,
            .EditorID = EditorID,
            .IsOverride = IsOverride,
            .IsNew = IsNew,
            .IsModified = IsModified
        }
        c.ItemFormIDs.AddRange(ItemFormIDs)
        For Each kv In LvliRealization
            c.LvliRealization(kv.Key) = New List(Of UInteger)(kv.Value)
        Next
        Return c
    End Function

End Class
