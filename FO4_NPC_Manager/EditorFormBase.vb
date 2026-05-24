''' <summary>
''' Shared base for the two modal NPC editors (EditFace_Form, EditBody_Form). Holds only the
''' members that were declared identically in both forms — the per-editor render host, the two
''' event-suppression flags, and the OK/Cancel result flag. Form-specific state (ReadOnly
''' ctor-set snapshots, the divergent refresh-throttle: _pendingScopes vs _pendingRefresh, and
''' each form's own UI fields) stays in the derived forms, which intentionally evolve apart
''' (face FMIN/FMRS vs body MRSV/BodySlide). The derived Designer partials change their
''' `Inherits System.Windows.Forms.Form` to `Inherits EditorFormBase`.
''' </summary>
Public Class EditorFormBase
    Inherits System.Windows.Forms.Form

    ''' <summary>Per-editor render host driving the embedded preview (each editor owns its own,
    ''' not the MainForm's _renderHost). Created in the derived form's Shown handler. Friend (not
    ''' Protected) because NpcRenderHost is Friend — a Public base can't expose it via Protected,
    ''' and both editor forms are in this assembly so Friend is accessible to them.</summary>
    Friend _editorHost As NpcRenderHost = Nothing

    ''' <summary>Guards UI event handlers from firing while we programmatically set control values
    ''' (slider/combo seeding, rollback). Set True around the bulk assignment, reset in Finally.</summary>
    Protected _suspendEvents As Boolean

    ''' <summary>Guards the render-toggle CheckedChanged handlers while we seed the editor's
    ''' visibility checkboxes from MainForm at Shown, so each assignment doesn't trigger a
    ''' redundant visibility pass before the first render.</summary>
    Protected _seedingToggles As Boolean

    ''' <summary>Set True when the user confirms (OK); MainForm reads this after ShowDialog to
    ''' decide whether to re-render its main preview from the (now-mutated) overlay. Cancel rolls
    ''' the overlay back so the MainForm preview is already correct without a reload.</summary>
    Public Property HasUncommittedChanges As Boolean = False

End Class
