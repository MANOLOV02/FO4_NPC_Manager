Imports FO4_Base_Library

''' <summary>Modal editor for los dos campos de UNA entrada CNTO (Item FormID + s32 Count) de la lista de
''' inventario de un NPC, abierto desde la pestaña "Inventory" del editor de NPC (botón Add/Edit / doble
''' clic en una fila).
'''
''' Entran y salen los dos VALORES, no una entrada: la entrada es un nodo del record y la escribe el que
''' llama, recién con OK. Por eso el bloque COED opcional queda intacto — este formulario edita nada más
''' Item y Count. El picker de item es un FormIdPicker sobre las signatures usuales de inventario.</summary>
Public Class NpcInventoryEntryEditor_Form

    ''' <summary>Common signatures an NPC inventory CNTO entry may reference (per the schema's
    ''' allowed-record list for the Item field). Not exhaustive — "Show all" is not offered —
    ''' but covers the vanilla item classes an author is likely to add. LVLI lets a leveled
    ''' item list be added directly.</summary>
    Private Shared ReadOnly ItemSignatures As String() =
        {"ARMO", "WEAP", "AMMO", "ALCH", "MISC", "BOOK", "KEYM", "NOTE", "INGR", "OMOD", "LVLI"}

    Private ReadOnly _mainForm As MainForm
    Private _itemFormID As UInteger

    ''' <summary>El item elegido, válido sólo tras <c>DialogResult.OK</c>.</summary>
    Public ReadOnly Property ResultFormID As UInteger
        Get
            Return _resultFormID
        End Get
    End Property
    Private _resultFormID As UInteger

    ''' <summary>La cantidad elegida, válida sólo tras <c>DialogResult.OK</c>.</summary>
    Public ReadOnly Property ResultCount As Integer
        Get
            Return _resultCount
        End Get
    End Property
    Private _resultCount As Integer

    ''' <param name="mainForm">Owner — supplies the PluginManager for the item picker + display names.</param>
    ''' <param name="itemFormID">El item con el que arranca el formulario (0 = ninguno).</param>
    ''' <param name="count">La cantidad con la que arranca el formulario.</param>
    Public Sub New(mainForm As MainForm, itemFormID As UInteger, count As Integer)
        InitializeComponent()
        _mainForm = mainForm

        _itemFormID = itemFormID
        NumCount.Value = ClampDec(CDec(count), NumCount)
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
        _resultFormID = _itemFormID
        _resultCount = CInt(NumCount.Value)
        DialogResult = DialogResult.OK
        Close()
    End Sub

    Private Shared Function ClampDec(v As Decimal, num As NumericUpDown) As Decimal
        If v < num.Minimum Then Return num.Minimum
        If v > num.Maximum Then Return num.Maximum
        Return v
    End Function

End Class
