Imports System.Linq
Imports FO4_Base_Library

''' <summary>Modal editor for a SINGLE <see cref="ARMO_AddonEntry"/> (Addon Index + ARMA reference) of an
''' ARMO, opened from the ARMO Editor's Addons tab (Edit button / double-click a row). Replaces the OLD
''' inline-editable INDX cell in GridAddons so the addons grid can be pure read-only.
'''
''' A working copy is edited in place and copied out into <see cref="ResultEntry"/> on OK; a Cancel never
''' mutates the caller's entry. The ARMA picker is race-filtered exactly like the ARMO Editor's "Add ARMA"
''' action (<see cref="MainForm.IsArmaRaceCompatible"/>) and includes the user's in-memory ARMA drafts.</summary>
Public Class ArmoAddonEditor_Form

    Private ReadOnly _mainForm As MainForm
    Private ReadOnly _raceFormID As UInteger
    Private _armaFormID As UInteger

    ''' <summary>The ARMO that owns this addon (the ARMO being edited in the parent ArmoEditor), threaded to the
    ''' ARMA editor so its "Full armor" preview renders the whole parent ARMO. 0 = no parent in context.</summary>
    Private ReadOnly _parentArmoFormID As UInteger

    ''' <summary>The OTFT of the outfit being assembled in the OutfitPicker, threaded to the ARMA editor so its
    ''' "Full Outfit" preview renders that outfit. 0 = no outfit in context.</summary>
    Private ReadOnly _outfitContextFormID As UInteger

    ''' <summary>The edited addon entry, valid only after <c>DialogResult.OK</c>. A fresh copy — caller owns it.</summary>
    Public ReadOnly Property ResultEntry As ARMO_AddonEntry
        Get
            Return _result
        End Get
    End Property
    Private _result As ARMO_AddonEntry

    ''' <param name="mainForm">Owner — supplies the PluginManager for the ARMA picker + display names + drafts.</param>
    ''' <param name="raceFormID">The editing ARMO's race — race-filters the ARMA picker like "Add ARMA".</param>
    ''' <param name="entry">The addon entry to edit. DEEP-COPIED in (never aliased); Nothing starts empty.</param>
    ''' <param name="parentArmoFormID">The owning ARMO's FormID — threaded to the ARMA editor's "Full armor"
    ''' preview. 0 = none.</param>
    ''' <param name="outfitContextFormID">The assembled-outfit OTFT — threaded to the ARMA editor's "Full Outfit"
    ''' preview. 0 = none.</param>
    Public Sub New(mainForm As MainForm, raceFormID As UInteger, entry As ARMO_AddonEntry,
                   Optional parentArmoFormID As UInteger = 0UI, Optional outfitContextFormID As UInteger = 0UI)
        InitializeComponent()
        _mainForm = mainForm
        _raceFormID = raceFormID
        _parentArmoFormID = parentArmoFormID
        _outfitContextFormID = outfitContextFormID

        Dim src = If(entry, New ARMO_AddonEntry())
        _armaFormID = src.ArmaFormID
        NumIndex.Value = ClampDec(src.AddonIndex, NumIndex)
        RenderArma()

        AddHandler ButtonPickArma.Click, AddressOf OnPickArma
        AddHandler ButtonEditArma.Click, AddressOf OnEditArma
        AddHandler ButtonOk.Click, AddressOf OnOk
    End Sub

    Private Sub RenderArma()
        LabelArmaValue.Text = If(_armaFormID = 0UI, "(none)",
                                 $"{_mainForm.GetRecordDisplayNameForEditor(_armaFormID)} [0x{_armaFormID:X8}]")
        ' Deep-edit is only meaningful when a real ARMA is referenced.
        ButtonEditArma.Enabled = (_armaFormID <> 0UI)
    End Sub

    ''' <summary>"Edit ARMA…" → open the standalone <see cref="ArmaEditor_Form"/> for the referenced ARMA, same
    ''' mechanic as the ARMO editor's old addon double-click: an existing draft opens directly (editDraft); a REAL
    ''' ARMA opens pre-loaded as the template (initialTemplateArmaFormID). On OK with a committed result, rewire
    ''' this modal's ARMA reference to the resulting draft/override FormID.</summary>
    Private Sub OnEditArma(sender As Object, e As EventArgs)
        If _armaFormID = 0UI Then Return   ' button is disabled in this state; guard anyway

        Dim previewNpc As UInteger
        Dim isFemale As Boolean
        _mainForm.GetEditorPreviewContext(previewNpc, isFemale)

        Dim armaDraft = _mainForm.TryGetArmaDraft(_armaFormID)
        Dim dlg As ArmaEditor_Form
        If armaDraft IsNot Nothing Then
            dlg = New ArmaEditor_Form(_mainForm, previewNpc, _raceFormID, isFemale, editDraft:=armaDraft,
                                      parentArmoFormID:=_parentArmoFormID, outfitContextFormID:=_outfitContextFormID)
        Else
            dlg = New ArmaEditor_Form(_mainForm, previewNpc, _raceFormID, isFemale,
                                      initialTemplateArmaFormID:=_armaFormID,
                                      parentArmoFormID:=_parentArmoFormID, outfitContextFormID:=_outfitContextFormID)
        End If

        Using dlg
            If dlg.ShowDialog(Me) = DialogResult.OK AndAlso dlg.ResultArmaFormID <> 0UI Then
                _armaFormID = dlg.ResultArmaFormID
                RenderArma()
            End If
        End Using
    End Sub

    ''' <summary>ARMA picker — race-filtered (+ ARMA drafts), same contract as ArmoEditor's "Add ARMA".</summary>
    Private Sub OnPickArma(sender As Object, e As EventArgs)
        Dim drafts = _mainForm.ArmaDrafts().Select(Function(d) New FormIdPickerEntry With {
            .FormID = d.FormID, .EditorID = d.EditorID, .DisplayName = d.EditorID, .Signature = "ARMA"}).ToList()
        Using dlg As New FormIdPicker_Form(_mainForm.PluginManagerForEditor, {"ARMA"},
                                           "Select Armor Addon (ARMA)", _armaFormID, allowNull:=False,
                                           extraDraftEntries:=drafts,
                                           formIdFilter:=Function(fid) _mainForm.IsArmaRaceCompatible(fid, _raceFormID))
            If dlg.ShowDialog(Me) <> DialogResult.OK OrElse dlg.SelectedFormID = 0UI Then Return
            _armaFormID = dlg.SelectedFormID
            RenderArma()
        End Using
    End Sub

    Private Sub OnOk(sender As Object, e As EventArgs)
        _result = New ARMO_AddonEntry With {.AddonIndex = CUShort(NumIndex.Value), .ArmaFormID = _armaFormID}
        DialogResult = DialogResult.OK
        Close()
    End Sub

    Private Shared Function ClampDec(v As Integer, num As NumericUpDown) As Decimal
        Dim d As Decimal = CDec(v)
        If d < num.Minimum Then Return num.Minimum
        If d > num.Maximum Then Return num.Maximum
        Return d
    End Function

End Class
