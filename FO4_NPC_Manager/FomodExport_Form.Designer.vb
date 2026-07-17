<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class FomodExport_Form
    Inherits System.Windows.Forms.Form

    Private components As System.ComponentModel.IContainer

    Protected Overrides Sub Dispose(ByVal disposing As Boolean)
        Try
            If disposing AndAlso components IsNot Nothing Then
                components.Dispose()
            End If
        Finally
            MyBase.Dispose(disposing)
        End Try
    End Sub

    Private Sub InitializeComponent()
        LabelHeader = New Label()
        LabelWarning = New Label()
        LabelModName = New Label()
        TextBoxModName = New TextBox()
        LabelVersion = New Label()
        TextBoxVersion = New TextBox()
        LabelAuthor = New Label()
        TextBoxAuthor = New TextBox()
        LabelWebsite = New Label()
        TextBoxWebsite = New TextBox()
        LabelDescription = New Label()
        TextBoxDescription = New TextBox()
        PictureBoxScreenshot = New PictureBox()
        CheckBoxIncludeScreenshot = New CheckBox()
        LabelCredit = New Label()
        LabelFiles = New Label()
        GridManifest = New DataGridView()
        ButtonAddAsset = New Button()
        ButtonRemoveAsset = New Button()
        LabelValidation = New Label()
        ButtonExport = New Button()
        ButtonCancel = New Button()
        CType(GridManifest, ComponentModel.ISupportInitialize).BeginInit()
        CType(PictureBoxScreenshot, ComponentModel.ISupportInitialize).BeginInit()
        SuspendLayout()
        '
        ' LabelHeader
        '
        LabelHeader.Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right
        LabelHeader.Font = New Drawing.Font("Segoe UI", 9F, Drawing.FontStyle.Bold)
        LabelHeader.Location = New Drawing.Point(12, 9)
        LabelHeader.Name = "LabelHeader"
        LabelHeader.Size = New Drawing.Size(730, 18)
        LabelHeader.TabIndex = 0
        LabelHeader.Text = ""
        '
        ' LabelWarning — "Save ESP first" when the selected plugin has unsaved NPC changes.
        '
        LabelWarning.Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right
        LabelWarning.ForeColor = Drawing.Color.Firebrick
        LabelWarning.Location = New Drawing.Point(12, 29)
        LabelWarning.Name = "LabelWarning"
        LabelWarning.Size = New Drawing.Size(730, 18)
        LabelWarning.TabIndex = 1
        LabelWarning.Text = ""
        LabelWarning.Visible = False
        '
        ' LabelModName
        '
        LabelModName.AutoSize = True
        LabelModName.Location = New Drawing.Point(12, 58)
        LabelModName.Name = "LabelModName"
        LabelModName.Size = New Drawing.Size(68, 15)
        LabelModName.TabIndex = 2
        LabelModName.Text = "Mod name:"
        '
        ' TextBoxModName
        '
        TextBoxModName.Location = New Drawing.Point(110, 55)
        TextBoxModName.Name = "TextBoxModName"
        TextBoxModName.Size = New Drawing.Size(400, 23)
        TextBoxModName.TabIndex = 3
        '
        ' LabelVersion
        '
        LabelVersion.Anchor = AnchorStyles.Top Or AnchorStyles.Right
        LabelVersion.AutoSize = True
        LabelVersion.Location = New Drawing.Point(534, 58)
        LabelVersion.Name = "LabelVersion"
        LabelVersion.Size = New Drawing.Size(48, 15)
        LabelVersion.TabIndex = 4
        LabelVersion.Text = "Version:"
        '
        ' TextBoxVersion
        '
        TextBoxVersion.Anchor = AnchorStyles.Top Or AnchorStyles.Right
        TextBoxVersion.Location = New Drawing.Point(592, 55)
        TextBoxVersion.Name = "TextBoxVersion"
        TextBoxVersion.Size = New Drawing.Size(150, 23)
        TextBoxVersion.TabIndex = 5
        '
        ' LabelAuthor
        '
        LabelAuthor.AutoSize = True
        LabelAuthor.Location = New Drawing.Point(12, 87)
        LabelAuthor.Name = "LabelAuthor"
        LabelAuthor.Size = New Drawing.Size(47, 15)
        LabelAuthor.TabIndex = 6
        LabelAuthor.Text = "Author:"
        '
        ' TextBoxAuthor
        '
        TextBoxAuthor.Location = New Drawing.Point(110, 84)
        TextBoxAuthor.Name = "TextBoxAuthor"
        TextBoxAuthor.Size = New Drawing.Size(400, 23)
        TextBoxAuthor.TabIndex = 7
        '
        ' LabelWebsite
        '
        LabelWebsite.AutoSize = True
        LabelWebsite.Location = New Drawing.Point(12, 116)
        LabelWebsite.Name = "LabelWebsite"
        LabelWebsite.Size = New Drawing.Size(52, 15)
        LabelWebsite.TabIndex = 8
        LabelWebsite.Text = "Website:"
        '
        ' TextBoxWebsite
        '
        TextBoxWebsite.Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right
        TextBoxWebsite.Location = New Drawing.Point(110, 113)
        TextBoxWebsite.Name = "TextBoxWebsite"
        TextBoxWebsite.Size = New Drawing.Size(632, 23)
        TextBoxWebsite.TabIndex = 9
        '
        ' LabelDescription
        '
        LabelDescription.AutoSize = True
        LabelDescription.Location = New Drawing.Point(12, 145)
        LabelDescription.Name = "LabelDescription"
        LabelDescription.Size = New Drawing.Size(70, 15)
        LabelDescription.TabIndex = 10
        LabelDescription.Text = "Description:"
        '
        ' TextBoxDescription
        '
        TextBoxDescription.AcceptsReturn = True
        TextBoxDescription.Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right
        TextBoxDescription.Location = New Drawing.Point(110, 142)
        TextBoxDescription.Multiline = True
        TextBoxDescription.Name = "TextBoxDescription"
        TextBoxDescription.ScrollBars = ScrollBars.Vertical
        TextBoxDescription.Size = New Drawing.Size(450, 68)
        TextBoxDescription.TabIndex = 11
        '
        ' PictureBoxScreenshot — preview capture taken when the dialog opened (main viewport).
        '
        PictureBoxScreenshot.Anchor = AnchorStyles.Top Or AnchorStyles.Right
        PictureBoxScreenshot.BorderStyle = BorderStyle.FixedSingle
        PictureBoxScreenshot.Location = New Drawing.Point(570, 142)
        PictureBoxScreenshot.Name = "PictureBoxScreenshot"
        PictureBoxScreenshot.Size = New Drawing.Size(172, 97)
        PictureBoxScreenshot.SizeMode = PictureBoxSizeMode.Zoom
        PictureBoxScreenshot.TabIndex = 20
        PictureBoxScreenshot.TabStop = False
        '
        ' CheckBoxIncludeScreenshot
        '
        CheckBoxIncludeScreenshot.Anchor = AnchorStyles.Top Or AnchorStyles.Right
        CheckBoxIncludeScreenshot.AutoSize = True
        CheckBoxIncludeScreenshot.Location = New Drawing.Point(570, 243)
        CheckBoxIncludeScreenshot.Name = "CheckBoxIncludeScreenshot"
        CheckBoxIncludeScreenshot.Size = New Drawing.Size(129, 19)
        CheckBoxIncludeScreenshot.TabIndex = 12
        CheckBoxIncludeScreenshot.Text = "Include screenshot"
        CheckBoxIncludeScreenshot.UseVisualStyleBackColor = True
        '
        ' LabelCredit
        '
        LabelCredit.Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right
        LabelCredit.ForeColor = SystemColors.GrayText
        LabelCredit.Location = New Drawing.Point(110, 213)
        LabelCredit.Name = "LabelCredit"
        LabelCredit.Size = New Drawing.Size(450, 30)
        LabelCredit.TabIndex = 13
        LabelCredit.Text = "The exported FOMOD always adds the credit line: Created with NPC_Manager by ManoloV02"
        '
        ' LabelFiles
        '
        LabelFiles.AutoSize = True
        LabelFiles.Location = New Drawing.Point(12, 265)
        LabelFiles.Name = "LabelFiles"
        LabelFiles.Size = New Drawing.Size(90, 15)
        LabelFiles.TabIndex = 14
        LabelFiles.Text = "Files to include:"
        '
        ' GridManifest
        '
        GridManifest.AllowUserToAddRows = False
        GridManifest.AllowUserToDeleteRows = False
        GridManifest.AllowUserToResizeRows = False
        GridManifest.Anchor = AnchorStyles.Top Or AnchorStyles.Bottom Or AnchorStyles.Left Or AnchorStyles.Right
        GridManifest.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize
        GridManifest.EditMode = DataGridViewEditMode.EditProgrammatically
        GridManifest.Location = New Drawing.Point(12, 283)
        GridManifest.MultiSelect = False
        GridManifest.Name = "GridManifest"
        GridManifest.RowHeadersVisible = False
        GridManifest.SelectionMode = DataGridViewSelectionMode.FullRowSelect
        GridManifest.Size = New Drawing.Size(730, 199)
        GridManifest.TabIndex = 15
        '
        ' ButtonAddAsset
        '
        ButtonAddAsset.Anchor = AnchorStyles.Bottom Or AnchorStyles.Left
        ButtonAddAsset.Location = New Drawing.Point(12, 490)
        ButtonAddAsset.Name = "ButtonAddAsset"
        ButtonAddAsset.Size = New Drawing.Size(110, 28)
        ButtonAddAsset.TabIndex = 15
        ButtonAddAsset.Text = "Add asset…"
        ButtonAddAsset.UseVisualStyleBackColor = True
        '
        ' ButtonRemoveAsset
        '
        ButtonRemoveAsset.Anchor = AnchorStyles.Bottom Or AnchorStyles.Left
        ButtonRemoveAsset.Enabled = False
        ButtonRemoveAsset.Location = New Drawing.Point(128, 490)
        ButtonRemoveAsset.Name = "ButtonRemoveAsset"
        ButtonRemoveAsset.Size = New Drawing.Size(110, 28)
        ButtonRemoveAsset.TabIndex = 16
        ButtonRemoveAsset.Text = "Remove asset"
        ButtonRemoveAsset.UseVisualStyleBackColor = True
        '
        ' LabelValidation
        '
        LabelValidation.Anchor = AnchorStyles.Bottom Or AnchorStyles.Left Or AnchorStyles.Right
        LabelValidation.ForeColor = Drawing.Color.Firebrick
        LabelValidation.Location = New Drawing.Point(12, 524)
        LabelValidation.Name = "LabelValidation"
        LabelValidation.Size = New Drawing.Size(730, 46)
        LabelValidation.TabIndex = 17
        LabelValidation.Text = ""
        '
        ' ButtonExport
        '
        ButtonExport.Anchor = AnchorStyles.Bottom Or AnchorStyles.Right
        ButtonExport.Enabled = False
        ButtonExport.Location = New Drawing.Point(536, 576)
        ButtonExport.Name = "ButtonExport"
        ButtonExport.Size = New Drawing.Size(120, 28)
        ButtonExport.TabIndex = 18
        ButtonExport.Text = "Export to ZIP…"
        ButtonExport.UseVisualStyleBackColor = True
        '
        ' ButtonCancel
        '
        ButtonCancel.Anchor = AnchorStyles.Bottom Or AnchorStyles.Right
        ButtonCancel.DialogResult = DialogResult.Cancel
        ButtonCancel.Location = New Drawing.Point(662, 576)
        ButtonCancel.Name = "ButtonCancel"
        ButtonCancel.Size = New Drawing.Size(80, 28)
        ButtonCancel.TabIndex = 19
        ButtonCancel.Text = "Cancel"
        ButtonCancel.UseVisualStyleBackColor = True
        '
        ' FomodExport_Form
        '
        AcceptButton = ButtonExport
        AutoScaleDimensions = New SizeF(7F, 15F)
        AutoScaleMode = AutoScaleMode.Font
        CancelButton = ButtonCancel
        ClientSize = New Drawing.Size(754, 616)
        Controls.Add(ButtonCancel)
        Controls.Add(ButtonExport)
        Controls.Add(LabelValidation)
        Controls.Add(ButtonRemoveAsset)
        Controls.Add(ButtonAddAsset)
        Controls.Add(GridManifest)
        Controls.Add(LabelFiles)
        Controls.Add(LabelCredit)
        Controls.Add(CheckBoxIncludeScreenshot)
        Controls.Add(PictureBoxScreenshot)
        Controls.Add(TextBoxDescription)
        Controls.Add(LabelDescription)
        Controls.Add(TextBoxWebsite)
        Controls.Add(LabelWebsite)
        Controls.Add(TextBoxAuthor)
        Controls.Add(LabelAuthor)
        Controls.Add(TextBoxVersion)
        Controls.Add(LabelVersion)
        Controls.Add(TextBoxModName)
        Controls.Add(LabelModName)
        Controls.Add(LabelWarning)
        Controls.Add(LabelHeader)
        MinimumSize = New Drawing.Size(640, 540)
        Name = "FomodExport_Form"
        StartPosition = FormStartPosition.CenterParent
        Text = "Export FOMOD"
        CType(GridManifest, ComponentModel.ISupportInitialize).EndInit()
        CType(PictureBoxScreenshot, ComponentModel.ISupportInitialize).EndInit()
        ResumeLayout(False)
        PerformLayout()
    End Sub

    Friend WithEvents LabelHeader As Label
    Friend WithEvents LabelWarning As Label
    Friend WithEvents LabelModName As Label
    Friend WithEvents TextBoxModName As TextBox
    Friend WithEvents LabelVersion As Label
    Friend WithEvents TextBoxVersion As TextBox
    Friend WithEvents LabelAuthor As Label
    Friend WithEvents TextBoxAuthor As TextBox
    Friend WithEvents LabelWebsite As Label
    Friend WithEvents TextBoxWebsite As TextBox
    Friend WithEvents LabelDescription As Label
    Friend WithEvents TextBoxDescription As TextBox
    Friend WithEvents PictureBoxScreenshot As PictureBox
    Friend WithEvents CheckBoxIncludeScreenshot As CheckBox
    Friend WithEvents LabelCredit As Label
    Friend WithEvents LabelFiles As Label
    Friend WithEvents GridManifest As DataGridView
    Friend WithEvents ButtonAddAsset As Button
    Friend WithEvents ButtonRemoveAsset As Button
    Friend WithEvents LabelValidation As Label
    Friend WithEvents ButtonExport As Button
    Friend WithEvents ButtonCancel As Button
End Class
