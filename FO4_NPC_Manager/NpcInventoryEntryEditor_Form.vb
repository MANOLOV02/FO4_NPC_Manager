Imports FO4_Base_Library

''' <summary>Modal editor for a SINGLE <see cref="NPC_InventoryItem"/> (CNTO Item FormID + s32 Count) of an
''' NPC's inventory list, opened from the NPC Editor's "Inventory" tab (Add/Edit button / double-click a row).
'''
''' A working copy is edited in place and copied out into <see cref="ResultEntry"/> on OK; a Cancel never
''' mutates the caller's entry (mirror of <see cref="ArmoDamageResistEditor_Form"/>). The Item picker is a
''' FormIdPicker over the common inventory-item signatures. The optional COED ownership block is preserved
''' verbatim from the source entry (this editor edits only Item + Count).</summary>
Public Class NpcInventoryEntryEditor_Form

    ''' <summary>Common signatures an NPC inventory CNTO entry may reference (per xEdit wbFormIDCk on
    ''' Item = wbInventoryItem's list). Not exhaustive — "Show all" is not offered — but covers the vanilla
    ''' item classes an author is likely to add. LVLI lets a leveled item list be added directly.</summary>
    Private Shared ReadOnly ItemSignatures As String() =
        {"ARMO", "WEAP", "AMMO", "ALCH", "MISC", "BOOK", "KEYM", "NOTE", "INGR", "OMOD", "LVLI"}

    Private ReadOnly _mainForm As MainForm
    Private ReadOnly _source As NPC_InventoryItem
    Private _itemFormID As UInteger

    ''' <summary>The edited inventory entry, valid only after <c>DialogResult.OK</c>. A fresh copy — caller owns it.</summary>
    Public ReadOnly Property ResultEntry As NPC_InventoryItem
        Get
            Return _result
        End Get
    End Property
    Private _result As NPC_InventoryItem

    ''' <param name="mainForm">Owner — supplies the PluginManager for the item picker + display names.</param>
    ''' <param name="entry">The inventory entry to edit. Copied in (never aliased); Nothing starts empty.</param>
    Public Sub New(mainForm As MainForm, entry As NPC_InventoryItem)
        InitializeComponent()
        _mainForm = mainForm

        _source = If(entry, New NPC_InventoryItem())
        _itemFormID = _source.ItemFormID
        NumCount.Value = ClampDec(CDec(_source.Count), NumCount)
        RenderItem()

        AddHandler ButtonPickItem.Click, AddressOf OnPickItem
        AddHandler ButtonOk.Click, AddressOf OnOk
    End Sub

    Private Sub RenderItem()
        TextBoxItem.Tag = _itemFormID
        TextBoxItem.Text = If(_itemFormID = 0UI, "(none)",
                              $"{_mainForm.GetRecordDisplayNameForEditor(_itemFormID)} [0x{_itemFormID:X8}]")
    End Sub

    ''' <summary>Item picker — FormIdPicker over the common inventory-item signatures. Required within a CNTO
    ''' entry (allowNull:=False); a cancelled/0 selection leaves the current value untouched.</summary>
    Private Sub OnPickItem(sender As Object, e As EventArgs)
        Using dlg As New FormIdPicker_Form(_mainForm.PluginManagerForEditor, ItemSignatures,
                                           "Select Inventory Item (CNTO)", _itemFormID, allowNull:=False)
            If dlg.ShowDialog(Me) <> DialogResult.OK OrElse dlg.SelectedFormID = 0UI Then Return
            _itemFormID = dlg.SelectedFormID
            RenderItem()
        End Using
    End Sub

    Private Sub OnOk(sender As Object, e As EventArgs)
        ' Item is required — a CNTO entry with a 0 (NULL) item is meaningless, so don't accept it.
        If _itemFormID = 0UI Then
            MessageBox.Show(Me, "Choose an Item for this inventory entry.", "Inventory",
                            MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If
        ' Preserve the optional COED ownership block verbatim from the source — this editor edits Item + Count only.
        _result = New NPC_InventoryItem With {
            .ItemFormID = _itemFormID,
            .Count = CInt(NumCount.Value),
            .HasCoed = _source.HasCoed,
            .CoedOwnerFormID = _source.CoedOwnerFormID,
            .CoedOwnerExtra = _source.CoedOwnerExtra,
            .CoedExtraIsFormID = _source.CoedExtraIsFormID,
            .CoedItemCondition = _source.CoedItemCondition}
        DialogResult = DialogResult.OK
        Close()
    End Sub

    Private Shared Function ClampDec(v As Decimal, num As NumericUpDown) As Decimal
        If v < num.Minimum Then Return num.Minimum
        If v > num.Maximum Then Return num.Maximum
        Return v
    End Function

End Class
