Imports System.Linq
Imports FO4_Base_Library

''' <summary>Modal picker over real ARMO / ARMA records from the load order — backs the Armor Editor's
''' "Override existing…" button (and, with <c>armaOnly:=True</c>, the "Add ARMA…" addon button). A type
''' toggle (ARMO / ARMA) swaps which record universe the filtered list shows; the chosen record's FormID +
''' kind are returned in <see cref="SelectedFormID"/> / <see cref="SelectedIsArma"/>. Filter by name,
''' EditorID, FormID or plugin (same fields as the other pickers).</summary>
Public Class OverridePicker_Form

    Private ReadOnly _mainForm As MainForm
    Private ReadOnly _armaOnly As Boolean
    Private _armoRecords As List(Of (FormID As UInteger, DisplayName As String, Plugin As String))
    Private _armaRecords As List(Of (FormID As UInteger, DisplayName As String, Plugin As String))

    ''' <summary>The picked record's FormID (0 on cancel / no selection).</summary>
    Public Property SelectedFormID As UInteger
    ''' <summary>True when the pick is an ARMA record, False for ARMO.</summary>
    Public Property SelectedIsArma As Boolean

    Public Sub New(mainForm As MainForm, Optional armaOnly As Boolean = False)
        InitializeComponent()
        _mainForm = mainForm
        _armaOnly = armaOnly

        _armoRecords = _mainForm.GetArmoRecordsForEditor()
        _armaRecords = _mainForm.GetArmaRecordsForEditor()

        If armaOnly Then
            Text = "Pick ARMA"
            RadioArmo.Visible = False
            RadioArma.Visible = False
            RadioArma.Checked = True
        Else
            Text = "Override existing ARMO / ARMA"
            RadioArmo.Checked = True
        End If

        AddHandler RadioArmo.CheckedChanged, AddressOf OnTypeChanged
        AddHandler RadioArma.CheckedChanged, AddressOf OnTypeChanged
        AddHandler TextBoxFilter.TextChanged, AddressOf OnFilterChanged
        AddHandler ListViewRecords.DoubleClick, AddressOf OnListDoubleClick
        AddHandler ButtonOk.Click, AddressOf OnOk

        RefreshList()
    End Sub

    Private ReadOnly Property ShowingArma As Boolean
        Get
            Return _armaOnly OrElse RadioArma.Checked
        End Get
    End Property

    Private Sub RefreshList()
        Dim src = If(ShowingArma, _armaRecords, _armoRecords)
        Dim text = TextBoxFilter.Text.Trim()
        Dim filtered = src
        If text.Length > 0 Then
            filtered = src.Where(Function(r) _
                r.DisplayName.Contains(text, StringComparison.OrdinalIgnoreCase) OrElse
                r.Plugin.Contains(text, StringComparison.OrdinalIgnoreCase) OrElse
                r.FormID.ToString("X8").Contains(text, StringComparison.OrdinalIgnoreCase)).ToList()
        End If

        ListViewRecords.BeginUpdate()
        Try
            ListViewRecords.Items.Clear()
            For Each r In filtered
                Dim row As New ListViewItem(r.DisplayName)
                row.SubItems.Add(r.FormID.ToString("X8"))
                row.SubItems.Add(r.Plugin)
                row.Tag = r.FormID
                ListViewRecords.Items.Add(row)
            Next
        Finally
            ListViewRecords.EndUpdate()
        End Try
    End Sub

    Private Sub OnTypeChanged(sender As Object, e As EventArgs)
        RefreshList()
    End Sub

    Private Sub OnFilterChanged(sender As Object, e As EventArgs)
        RefreshList()
    End Sub

    Private Sub OnListDoubleClick(sender As Object, e As EventArgs)
        If ListViewRecords.SelectedItems.Count = 0 Then Return
        OnOk(sender, e)
    End Sub

    Private Sub OnOk(sender As Object, e As EventArgs)
        If ListViewRecords.SelectedItems.Count = 0 Then
            DialogResult = DialogResult.None
            Return
        End If
        SelectedFormID = CUInt(ListViewRecords.SelectedItems(0).Tag)
        SelectedIsArma = ShowingArma
        DialogResult = DialogResult.OK
        Close()
    End Sub

End Class
