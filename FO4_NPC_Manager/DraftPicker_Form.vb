''' <summary>Generic modal picker over a list of (FormID, DisplayName) draft entries — used by the Armor
''' Editor to pick an existing ARMA draft to attach as an ARMO addon. Returns the chosen FormID in
''' <see cref="SelectedFormID"/> (0 on cancel).</summary>
Public Class DraftPicker_Form

    Private ReadOnly _items As List(Of (FormID As UInteger, DisplayName As String))

    ''' <summary>The picked draft FormID (0 on cancel / no selection).</summary>
    Public Property SelectedFormID As UInteger

    Public Sub New(title As String, items As List(Of (FormID As UInteger, DisplayName As String)))
        InitializeComponent()
        Text = title
        _items = If(items, New List(Of (FormID As UInteger, DisplayName As String)))

        AddHandler ListViewItems.DoubleClick, AddressOf OnListDoubleClick
        AddHandler ButtonOk.Click, AddressOf OnOk

        RefreshList()
    End Sub

    Private Sub RefreshList()
        ListViewItems.BeginUpdate()
        Try
            ListViewItems.Items.Clear()
            For Each it In _items
                Dim row As New ListViewItem(it.DisplayName)
                row.SubItems.Add(it.FormID.ToString("X8"))
                row.Tag = it.FormID
                ListViewItems.Items.Add(row)
            Next
        Finally
            ListViewItems.EndUpdate()
        End Try
    End Sub

    Private Sub OnListDoubleClick(sender As Object, e As EventArgs)
        If ListViewItems.SelectedItems.Count = 0 Then Return
        OnOk(sender, e)
    End Sub

    Private Sub OnOk(sender As Object, e As EventArgs)
        If ListViewItems.SelectedItems.Count = 0 Then
            DialogResult = DialogResult.None
            Return
        End If
        SelectedFormID = CUInt(ListViewItems.SelectedItems(0).Tag)
        DialogResult = DialogResult.OK
        Close()
    End Sub

End Class
