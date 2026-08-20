' UI built in Designer per 00-reglas-ui-y-vb.md.
<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class HeadPartPicker_Form
    ' Hereda de FO4_Base_Library.IconFormBase, que aporta los ImageList compartidos IconsSmall (16x16)
    ' e IconsLarge (24x24): los iconos viven UNA sola vez, en el resx de ese formulario base.
    ' El formulario base NO tiene controles y no fija Size/Text/Icon/AutoScale, asi que heredar de
    ' el no cambia el aspecto de nada. Ver el remarks de IconFormBase.vb.
    ' Los iconos se eligen SIEMPRE por ImageKey, nunca por ImageIndex: el orden del ImageList
    ' compartido se corre solo con agregar un PNG a Resources\Icons.
    Inherits FO4_Base_Library.IconFormBase

    <System.Diagnostics.DebuggerNonUserCode()>
    Protected Overrides Sub Dispose(disposing As Boolean)
        Try
            If disposing AndAlso components IsNot Nothing Then components.Dispose()
        Finally
            MyBase.Dispose(disposing)
        End Try
    End Sub

    Private components As System.ComponentModel.IContainer

    <System.Diagnostics.DebuggerStepThrough()>
    Private Sub InitializeComponent()
        RootLayout = New TableLayoutPanel()
        LabelHeader = New Label()
        TextBoxFilter = New TextBox()
        MainSplit = New SplitContainer()
        ListViewParts = New ListView()
        ColumnEditorID = New ColumnHeader()
        ColumnFullName = New ColumnHeader()
        ColumnPlugin = New ColumnHeader()
        ColumnFormID = New ColumnHeader()
        PreviewControlPanel = New Panel()
        BottomLayout = New FlowLayoutPanel()
        ButtonOk = New Button()
        ButtonCancel = New Button()
        RootLayout.SuspendLayout()
        CType(MainSplit, ComponentModel.ISupportInitialize).BeginInit()
        MainSplit.Panel1.SuspendLayout()
        MainSplit.Panel2.SuspendLayout()
        MainSplit.SuspendLayout()
        BottomLayout.SuspendLayout()
        SuspendLayout()
        ' 
        ' RootLayout
        ' 
        RootLayout.ColumnCount = 1
        RootLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        RootLayout.Controls.Add(LabelHeader, 0, 0)
        RootLayout.Controls.Add(TextBoxFilter, 0, 1)
        RootLayout.Controls.Add(MainSplit, 0, 2)
        RootLayout.Controls.Add(BottomLayout, 0, 3)
        RootLayout.Dock = DockStyle.Fill
        RootLayout.Location = New Point(0, 0)
        RootLayout.Name = "RootLayout"
        RootLayout.Padding = New Padding(8)
        RootLayout.RowCount = 4
        RootLayout.RowStyles.Add(New RowStyle())
        RootLayout.RowStyles.Add(New RowStyle())
        RootLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        RootLayout.RowStyles.Add(New RowStyle())
        RootLayout.Size = New Size(960, 540)
        RootLayout.TabIndex = 0
        ' 
        ' LabelHeader
        ' 
        LabelHeader.AutoSize = True
        LabelHeader.Location = New Point(8, 8)
        LabelHeader.Margin = New Padding(0, 0, 0, 4)
        LabelHeader.Name = "LabelHeader"
        LabelHeader.Size = New Size(91, 15)
        LabelHeader.TabIndex = 0
        LabelHeader.Text = "Select head part"
        ' 
        ' TextBoxFilter
        ' 
        TextBoxFilter.Dock = DockStyle.Top
        TextBoxFilter.Location = New Point(8, 27)
        TextBoxFilter.Margin = New Padding(0, 0, 0, 6)
        TextBoxFilter.Name = "TextBoxFilter"
        TextBoxFilter.PlaceholderText = "Filter by name or editor ID..."
        TextBoxFilter.Size = New Size(944, 23)
        TextBoxFilter.TabIndex = 1
        ' 
        ' MainSplit
        ' 
        MainSplit.Dock = DockStyle.Fill
        MainSplit.Location = New Point(11, 59)
        MainSplit.Name = "MainSplit"
        ' 
        ' MainSplit.Panel1
        ' 
        MainSplit.Panel1.Controls.Add(ListViewParts)
        ' 
        ' MainSplit.Panel2
        ' 
        MainSplit.Panel2.Controls.Add(PreviewControlPanel)
        MainSplit.Size = New Size(938, 429)
        MainSplit.SplitterDistance = 564
        MainSplit.TabIndex = 2
        ' 
        ' ListViewParts
        ' 
        ListViewParts.Columns.AddRange(New ColumnHeader() {ColumnEditorID, ColumnFullName, ColumnPlugin, ColumnFormID})
        ListViewParts.Dock = DockStyle.Fill
        ListViewParts.FullRowSelect = True
        ListViewParts.Location = New Point(0, 0)
        ListViewParts.MultiSelect = False
        ListViewParts.Name = "ListViewParts"
        ListViewParts.Size = New Size(564, 429)
        ListViewParts.TabIndex = 0
        ListViewParts.UseCompatibleStateImageBehavior = False
        ListViewParts.View = View.Details
        ' 
        ' ColumnEditorID
        ' 
        ColumnEditorID.Text = "Editor ID"
        ColumnEditorID.Width = 180
        ' 
        ' ColumnFullName
        ' 
        ColumnFullName.Text = "Name"
        ColumnFullName.Width = 180
        ' 
        ' ColumnPlugin
        ' 
        ColumnPlugin.Text = "Plugin"
        ColumnPlugin.Width = 110
        ' 
        ' ColumnFormID
        ' 
        ColumnFormID.Text = "FormID"
        ColumnFormID.Width = 70
        ' 
        ' PreviewControlPanel
        ' 
        PreviewControlPanel.BorderStyle = BorderStyle.FixedSingle
        PreviewControlPanel.Dock = DockStyle.Fill
        PreviewControlPanel.Location = New Point(0, 0)
        PreviewControlPanel.Name = "PreviewControlPanel"
        PreviewControlPanel.Size = New Size(370, 429)
        PreviewControlPanel.TabIndex = 0
        ' 
        ' BottomLayout
        ' 
        BottomLayout.AutoSize = True
        BottomLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink
        BottomLayout.Controls.Add(ButtonOk)
        BottomLayout.Controls.Add(ButtonCancel)
        BottomLayout.Dock = DockStyle.Fill
        BottomLayout.FlowDirection = FlowDirection.RightToLeft
        BottomLayout.Location = New Point(11, 494)
        BottomLayout.Name = "BottomLayout"
        BottomLayout.Padding = New Padding(0, 6, 0, 0)
        BottomLayout.Size = New Size(938, 35)
        BottomLayout.TabIndex = 3
        ' 
        ' ButtonOk
        ' 
        ButtonOk.DialogResult = DialogResult.OK
        ButtonOk.Location = New Point(855, 9)
        ButtonOk.Name = "ButtonOk"
        ButtonOk.Size = New Size(80, 23)
        ButtonOk.TabIndex = 0
        ButtonOk.Text = "OK"
        ' 
        ' ButtonCancel
        ' 
        ButtonCancel.DialogResult = DialogResult.Cancel
        ButtonCancel.Location = New Point(769, 9)
        ButtonCancel.Name = "ButtonCancel"
        ButtonCancel.Size = New Size(80, 23)
        ButtonCancel.TabIndex = 1
        ButtonCancel.Text = "Cancel"
        ' 
        ' HeadPartPicker_Form
        ' 
        AcceptButton = ButtonOk
        CancelButton = ButtonCancel
        ClientSize = New Size(960, 540)
        Controls.Add(RootLayout)
        MaximizeBox = False
        MinimizeBox = False
        Name = "HeadPartPicker_Form"
        StartPosition = FormStartPosition.CenterParent
        Text = "Add Head Part"
        RootLayout.ResumeLayout(False)
        RootLayout.PerformLayout()
        MainSplit.Panel1.ResumeLayout(False)
        MainSplit.Panel2.ResumeLayout(False)
        CType(MainSplit, ComponentModel.ISupportInitialize).EndInit()
        MainSplit.ResumeLayout(False)
        BottomLayout.ResumeLayout(False)
        ResumeLayout(False)
    End Sub

    Friend WithEvents RootLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents LabelHeader As System.Windows.Forms.Label
    Friend WithEvents TextBoxFilter As System.Windows.Forms.TextBox
    Friend WithEvents MainSplit As System.Windows.Forms.SplitContainer
    Friend WithEvents ListViewParts As System.Windows.Forms.ListView
    Friend WithEvents ColumnEditorID As System.Windows.Forms.ColumnHeader
    Friend WithEvents ColumnFullName As System.Windows.Forms.ColumnHeader
    Friend WithEvents ColumnPlugin As System.Windows.Forms.ColumnHeader
    Friend WithEvents ColumnFormID As System.Windows.Forms.ColumnHeader
    Friend WithEvents PreviewControlPanel As System.Windows.Forms.Panel
    Friend WithEvents ButtonOk As System.Windows.Forms.Button
    Friend WithEvents ButtonCancel As System.Windows.Forms.Button
    Friend WithEvents BottomLayout As System.Windows.Forms.FlowLayoutPanel
End Class
