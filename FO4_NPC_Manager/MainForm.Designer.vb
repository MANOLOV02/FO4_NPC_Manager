<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class MainForm
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
        Me.SplitContainer1 = New System.Windows.Forms.SplitContainer()
        Me.PanelLeft = New System.Windows.Forms.Panel()
        Me.TreeViewNPCs = New System.Windows.Forms.TreeView()
        Me.TextBoxSearch = New System.Windows.Forms.TextBox()
        Me.LabelSearch = New System.Windows.Forms.Label()
        Me.SplitContainerRight = New System.Windows.Forms.SplitContainer()
        Me.PanelRight = New System.Windows.Forms.Panel()
        Me.LabelStatus = New System.Windows.Forms.Label()
        Me.PanelRecordDetails = New System.Windows.Forms.Panel()
        Me.TreeViewRecordDetails = New System.Windows.Forms.TreeView()
        Me.LabelRecordTitle = New System.Windows.Forms.Label()
        Me.ComboBoxPreviewMode = New System.Windows.Forms.ComboBox()
        Me.LabelPreviewMode = New System.Windows.Forms.Label()
        Me.StatusStrip1 = New System.Windows.Forms.StatusStrip()
        Me.ToolStripStatusLabel1 = New System.Windows.Forms.ToolStripStatusLabel()
        Me.ToolStripProgressBar1 = New System.Windows.Forms.ToolStripProgressBar()
        CType(Me.SplitContainer1, System.ComponentModel.ISupportInitialize).BeginInit()
        Me.SplitContainer1.Panel1.SuspendLayout()
        Me.SplitContainer1.Panel2.SuspendLayout()
        Me.SplitContainer1.SuspendLayout()
        Me.PanelLeft.SuspendLayout()
        CType(Me.SplitContainerRight, System.ComponentModel.ISupportInitialize).BeginInit()
        Me.SplitContainerRight.Panel1.SuspendLayout()
        Me.SplitContainerRight.Panel2.SuspendLayout()
        Me.SplitContainerRight.SuspendLayout()
        Me.PanelRight.SuspendLayout()
        Me.PanelRecordDetails.SuspendLayout()
        Me.StatusStrip1.SuspendLayout()
        Me.SuspendLayout()
        '
        ' SplitContainer1
        '
        Me.SplitContainer1.Dock = System.Windows.Forms.DockStyle.Fill
        Me.SplitContainer1.Location = New System.Drawing.Point(0, 0)
        Me.SplitContainer1.Name = "SplitContainer1"
        Me.SplitContainer1.SplitterDistance = 350
        '
        ' SplitContainer1.Panel1
        '
        Me.SplitContainer1.Panel1.Controls.Add(Me.PanelLeft)
        '
        ' SplitContainer1.Panel2
        '
        Me.SplitContainer1.Panel2.Controls.Add(Me.SplitContainerRight)
        Me.SplitContainer1.Size = New System.Drawing.Size(1200, 778)
        Me.SplitContainer1.TabIndex = 0
        '
        ' PanelLeft
        '
        Me.PanelLeft.Controls.Add(Me.TreeViewNPCs)
        Me.PanelLeft.Controls.Add(Me.TextBoxSearch)
        Me.PanelLeft.Controls.Add(Me.LabelSearch)
        Me.PanelLeft.Dock = System.Windows.Forms.DockStyle.Fill
        Me.PanelLeft.Location = New System.Drawing.Point(0, 0)
        Me.PanelLeft.Name = "PanelLeft"
        Me.PanelLeft.Size = New System.Drawing.Size(350, 778)
        '
        ' TreeViewNPCs
        '
        Me.TreeViewNPCs.Anchor = AnchorStyles.Top Or AnchorStyles.Bottom Or AnchorStyles.Left Or AnchorStyles.Right
        Me.TreeViewNPCs.Location = New System.Drawing.Point(3, 50)
        Me.TreeViewNPCs.Name = "TreeViewNPCs"
        Me.TreeViewNPCs.Size = New System.Drawing.Size(344, 725)
        Me.TreeViewNPCs.DrawMode = System.Windows.Forms.TreeViewDrawMode.OwnerDrawText
        '
        ' TextBoxSearch
        '
        Me.TextBoxSearch.Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right
        Me.TextBoxSearch.Location = New System.Drawing.Point(60, 14)
        Me.TextBoxSearch.Name = "TextBoxSearch"
        Me.TextBoxSearch.Size = New System.Drawing.Size(287, 23)
        Me.TextBoxSearch.PlaceholderText = "Filter NPCs..."
        '
        ' LabelSearch
        '
        Me.LabelSearch.AutoSize = True
        Me.LabelSearch.Location = New System.Drawing.Point(3, 17)
        Me.LabelSearch.Name = "LabelSearch"
        Me.LabelSearch.Text = "Search:"
        '
        ' SplitContainerRight (horizontal split: top=3D preview, bottom=record details)
        '
        Me.SplitContainerRight.Dock = System.Windows.Forms.DockStyle.Fill
        Me.SplitContainerRight.Location = New System.Drawing.Point(0, 0)
        Me.SplitContainerRight.Name = "SplitContainerRight"
        Me.SplitContainerRight.Orientation = System.Windows.Forms.Orientation.Horizontal
        Me.SplitContainerRight.SplitterDistance = 420
        '
        ' SplitContainerRight.Panel1 - 3D Preview
        '
        Me.SplitContainerRight.Panel1.Controls.Add(Me.PanelRight)
        Me.SplitContainerRight.Panel1MinSize = 150
        '
        ' SplitContainerRight.Panel2 - Record Details
        '
        Me.SplitContainerRight.Panel2.Controls.Add(Me.PanelRecordDetails)
        Me.SplitContainerRight.Panel2MinSize = 150
        Me.SplitContainerRight.Size = New System.Drawing.Size(846, 778)
        '
        ' PanelPreviewToolbar (TableLayoutPanel, 2 rows, above 3D preview)
        '
        Me.ComboBoxOutfit = New System.Windows.Forms.ComboBox()
        Me.LabelOutfit = New System.Windows.Forms.Label()
        Me.ButtonReroll = New System.Windows.Forms.Button()
        Me.ButtonRandomNPC = New System.Windows.Forms.Button()
        Me.ComboBoxGender = New System.Windows.Forms.ComboBox()
        Me.CheckBoxApplyBoneMorphs = New System.Windows.Forms.CheckBox()
        Me.CheckBoxApplyVertexMorphs = New System.Windows.Forms.CheckBox()
        Me.PanelPreviewToolbar = New System.Windows.Forms.TableLayoutPanel()
        Me.PanelPreviewToolbar.Dock = System.Windows.Forms.DockStyle.Top
        Me.PanelPreviewToolbar.AutoSize = True
        Me.PanelPreviewToolbar.Name = "PanelPreviewToolbar"
        Me.PanelPreviewToolbar.ColumnCount = 4
        Me.PanelPreviewToolbar.RowCount = 3
        Me.PanelPreviewToolbar.ColumnStyles.Add(New System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.AutoSize))
        Me.PanelPreviewToolbar.ColumnStyles.Add(New System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.AutoSize))
        Me.PanelPreviewToolbar.ColumnStyles.Add(New System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100.0!))
        Me.PanelPreviewToolbar.ColumnStyles.Add(New System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.AutoSize))
        Me.PanelPreviewToolbar.RowStyles.Add(New System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize))
        Me.PanelPreviewToolbar.RowStyles.Add(New System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize))
        Me.PanelPreviewToolbar.RowStyles.Add(New System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize))
        ' Row 0: Preview label + combo + gender combo + random NPC button
        Me.PanelPreviewToolbar.Controls.Add(Me.LabelPreviewMode, 0, 0)
        Me.PanelPreviewToolbar.Controls.Add(Me.ComboBoxPreviewMode, 1, 0)
        Me.PanelPreviewToolbar.Controls.Add(Me.ComboBoxGender, 2, 0)
        Me.PanelPreviewToolbar.Controls.Add(Me.ButtonRandomNPC, 3, 0)
        ' Row 1: Outfit label + combo + reroll button
        Me.PanelPreviewToolbar.Controls.Add(Me.LabelOutfit, 0, 1)
        Me.PanelPreviewToolbar.SetColumnSpan(Me.ComboBoxOutfit, 2)
        Me.PanelPreviewToolbar.Controls.Add(Me.ComboBoxOutfit, 1, 1)
        Me.PanelPreviewToolbar.Controls.Add(Me.ButtonReroll, 3, 1)
        ' Row 2: Bone morphs checkbox (col 0-1) + Vertex morphs checkbox (col 2-3)
        Me.PanelPreviewToolbar.SetColumnSpan(Me.CheckBoxApplyBoneMorphs, 2)
        Me.PanelPreviewToolbar.Controls.Add(Me.CheckBoxApplyBoneMorphs, 0, 2)
        Me.PanelPreviewToolbar.SetColumnSpan(Me.CheckBoxApplyVertexMorphs, 2)
        Me.PanelPreviewToolbar.Controls.Add(Me.CheckBoxApplyVertexMorphs, 2, 2)
        '
        ' CheckBoxApplyBoneMorphs
        '
        Me.CheckBoxApplyBoneMorphs.AutoSize = True
        Me.CheckBoxApplyBoneMorphs.Checked = True
        Me.CheckBoxApplyBoneMorphs.CheckState = System.Windows.Forms.CheckState.Checked
        Me.CheckBoxApplyBoneMorphs.Name = "CheckBoxApplyBoneMorphs"
        Me.CheckBoxApplyBoneMorphs.Text = "FMRS bone morphs"
        '
        ' CheckBoxApplyVertexMorphs
        '
        Me.CheckBoxApplyVertexMorphs.AutoSize = True
        Me.CheckBoxApplyVertexMorphs.Checked = True
        Me.CheckBoxApplyVertexMorphs.CheckState = System.Windows.Forms.CheckState.Checked
        Me.CheckBoxApplyVertexMorphs.Name = "CheckBoxApplyVertexMorphs"
        Me.CheckBoxApplyVertexMorphs.Text = "Vertex morphs (chargen TRI)"
        '
        ' PanelRight
        '
        Me.PanelRight.Controls.Add(Me.LabelStatus)
        Me.PanelRight.Controls.Add(Me.PanelPreviewToolbar)
        Me.PanelRight.Dock = System.Windows.Forms.DockStyle.Fill
        Me.PanelRight.Location = New System.Drawing.Point(0, 0)
        Me.PanelRight.Name = "PanelRight"
        Me.PanelRight.Size = New System.Drawing.Size(846, 420)
        '
        ' LabelPreviewMode
        '
        Me.LabelPreviewMode.AutoSize = True
        Me.LabelPreviewMode.Anchor = AnchorStyles.Left
        Me.LabelPreviewMode.Name = "LabelPreviewMode"
        Me.LabelPreviewMode.Text = "Preview:"
        '
        ' ComboBoxPreviewMode
        '
        Me.ComboBoxPreviewMode.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList
        Me.ComboBoxPreviewMode.Items.AddRange(New Object() {"Full Character", "Only Face"})
        Me.ComboBoxPreviewMode.Name = "ComboBoxPreviewMode"
        Me.ComboBoxPreviewMode.Size = New System.Drawing.Size(140, 23)
        Me.ComboBoxPreviewMode.SelectedIndex = 0
        '
        ' LabelOutfit
        '
        Me.LabelOutfit.AutoSize = True
        Me.LabelOutfit.Anchor = AnchorStyles.Left
        Me.LabelOutfit.Name = "LabelOutfit"
        Me.LabelOutfit.Text = "Outfit:"
        '
        ' ComboBoxOutfit
        '
        Me.ComboBoxOutfit.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList
        Me.ComboBoxOutfit.Dock = System.Windows.Forms.DockStyle.Fill
        Me.ComboBoxOutfit.Name = "ComboBoxOutfit"
        '
        ' ButtonReroll
        '
        Me.ButtonReroll.Text = "?"
        Me.ButtonReroll.Font = New System.Drawing.Font("Segoe UI", 9.0!, System.Drawing.FontStyle.Bold)
        Me.ButtonReroll.Size = New System.Drawing.Size(30, 25)
        Me.ButtonReroll.FlatStyle = System.Windows.Forms.FlatStyle.Flat
        '
        ' ButtonRandomNPC (randomize template NPC from LVLN)
        '
        Me.ButtonRandomNPC.Text = "?"
        Me.ButtonRandomNPC.Font = New System.Drawing.Font("Segoe UI", 9.0!, System.Drawing.FontStyle.Bold)
        Me.ButtonRandomNPC.Size = New System.Drawing.Size(30, 25)
        Me.ButtonRandomNPC.FlatStyle = System.Windows.Forms.FlatStyle.Flat
        '
        ' ComboBoxGender (filter for LVLN random: Random/Male/Female)
        '
        Me.ComboBoxGender.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList
        Me.ComboBoxGender.Items.AddRange(New Object() {"Random", "Male", "Female"})
        Me.ComboBoxGender.Name = "ComboBoxGender"
        Me.ComboBoxGender.Size = New System.Drawing.Size(80, 23)
        Me.ComboBoxGender.SelectedIndex = 0
        '
        ' LabelStatus
        '
        Me.LabelStatus.Dock = System.Windows.Forms.DockStyle.Fill
        Me.LabelStatus.Font = New System.Drawing.Font("Segoe UI", 14.0!, System.Drawing.FontStyle.Regular)
        Me.LabelStatus.Name = "LabelStatus"
        Me.LabelStatus.Text = "Loading..."
        Me.LabelStatus.TextAlign = System.Drawing.ContentAlignment.MiddleCenter
        '
        ' PanelRecordDetails
        '
        Me.PanelRecordDetails.Controls.Add(Me.TreeViewRecordDetails)
        Me.PanelRecordDetails.Controls.Add(Me.LabelRecordTitle)
        Me.PanelRecordDetails.Dock = System.Windows.Forms.DockStyle.Fill
        Me.PanelRecordDetails.Location = New System.Drawing.Point(0, 0)
        Me.PanelRecordDetails.Name = "PanelRecordDetails"
        Me.PanelRecordDetails.Size = New System.Drawing.Size(846, 354)
        '
        ' LabelRecordTitle
        '
        Me.LabelRecordTitle.Dock = System.Windows.Forms.DockStyle.Top
        Me.LabelRecordTitle.Font = New System.Drawing.Font("Segoe UI Semibold", 9.75!, System.Drawing.FontStyle.Bold)
        Me.LabelRecordTitle.Location = New System.Drawing.Point(0, 0)
        Me.LabelRecordTitle.Name = "LabelRecordTitle"
        Me.LabelRecordTitle.Size = New System.Drawing.Size(846, 24)
        Me.LabelRecordTitle.Text = "  Record Details"
        Me.LabelRecordTitle.TextAlign = System.Drawing.ContentAlignment.MiddleLeft
        Me.LabelRecordTitle.BackColor = System.Drawing.SystemColors.ControlDark
        Me.LabelRecordTitle.ForeColor = System.Drawing.SystemColors.ControlLightLight
        '
        ' TreeViewRecordDetails
        '
        Me.TreeViewRecordDetails.Dock = System.Windows.Forms.DockStyle.Fill
        Me.TreeViewRecordDetails.Location = New System.Drawing.Point(0, 24)
        Me.TreeViewRecordDetails.Name = "TreeViewRecordDetails"
        Me.TreeViewRecordDetails.Size = New System.Drawing.Size(846, 330)
        Me.TreeViewRecordDetails.Font = New System.Drawing.Font("Cascadia Code", 8.5!, System.Drawing.FontStyle.Regular)
        '
        ' StatusStrip1
        '
        Me.StatusStrip1.Items.AddRange(New System.Windows.Forms.ToolStripItem() {Me.ToolStripStatusLabel1, Me.ToolStripProgressBar1})
        Me.StatusStrip1.Location = New System.Drawing.Point(0, 778)
        Me.StatusStrip1.Name = "StatusStrip1"
        Me.StatusStrip1.Size = New System.Drawing.Size(1200, 22)
        '
        ' ToolStripStatusLabel1
        '
        Me.ToolStripStatusLabel1.Name = "ToolStripStatusLabel1"
        Me.ToolStripStatusLabel1.Size = New System.Drawing.Size(1083, 17)
        Me.ToolStripStatusLabel1.Spring = True
        Me.ToolStripStatusLabel1.TextAlign = System.Drawing.ContentAlignment.MiddleLeft
        Me.ToolStripStatusLabel1.Text = "Ready"
        '
        ' ToolStripProgressBar1
        '
        Me.ToolStripProgressBar1.Name = "ToolStripProgressBar1"
        Me.ToolStripProgressBar1.Size = New System.Drawing.Size(100, 16)
        Me.ToolStripProgressBar1.Visible = False
        '
        ' MainForm
        '
        Me.AutoScaleDimensions = New System.Drawing.SizeF(7.0!, 15.0!)
        Me.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font
        Me.ClientSize = New System.Drawing.Size(1200, 800)
        Me.Controls.Add(Me.SplitContainer1)
        Me.Controls.Add(Me.StatusStrip1)
        Me.Name = "MainForm"
        Me.Text = "FO4 NPC Manager"
        Me.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen
        CType(Me.SplitContainer1, System.ComponentModel.ISupportInitialize).EndInit()
        Me.SplitContainer1.Panel1.ResumeLayout(False)
        Me.SplitContainer1.Panel2.ResumeLayout(False)
        Me.SplitContainer1.ResumeLayout(False)
        Me.PanelLeft.ResumeLayout(False)
        Me.PanelLeft.PerformLayout()
        CType(Me.SplitContainerRight, System.ComponentModel.ISupportInitialize).EndInit()
        Me.SplitContainerRight.Panel1.ResumeLayout(False)
        Me.SplitContainerRight.Panel2.ResumeLayout(False)
        Me.SplitContainerRight.ResumeLayout(False)
        Me.PanelRight.ResumeLayout(False)
        Me.PanelRecordDetails.ResumeLayout(False)
        Me.StatusStrip1.ResumeLayout(False)
        Me.StatusStrip1.PerformLayout()
        Me.ResumeLayout(False)
        Me.PerformLayout()
    End Sub

    Friend WithEvents SplitContainer1 As System.Windows.Forms.SplitContainer
    Friend WithEvents PanelLeft As System.Windows.Forms.Panel
    Friend WithEvents TreeViewNPCs As System.Windows.Forms.TreeView
    Friend WithEvents TextBoxSearch As System.Windows.Forms.TextBox
    Friend WithEvents LabelSearch As System.Windows.Forms.Label
    Friend WithEvents SplitContainerRight As System.Windows.Forms.SplitContainer
    Friend WithEvents PanelRight As System.Windows.Forms.Panel
    Friend WithEvents LabelStatus As System.Windows.Forms.Label
    Friend WithEvents PanelRecordDetails As System.Windows.Forms.Panel
    Friend WithEvents TreeViewRecordDetails As System.Windows.Forms.TreeView
    Friend WithEvents LabelRecordTitle As System.Windows.Forms.Label
    Friend WithEvents PanelPreviewToolbar As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents ComboBoxPreviewMode As System.Windows.Forms.ComboBox
    Friend WithEvents LabelPreviewMode As System.Windows.Forms.Label
    Friend WithEvents ComboBoxOutfit As System.Windows.Forms.ComboBox
    Friend WithEvents LabelOutfit As System.Windows.Forms.Label
    Friend WithEvents ButtonReroll As System.Windows.Forms.Button
    Friend WithEvents ButtonRandomNPC As System.Windows.Forms.Button
    Friend WithEvents ComboBoxGender As System.Windows.Forms.ComboBox
    Friend WithEvents CheckBoxApplyBoneMorphs As System.Windows.Forms.CheckBox
    Friend WithEvents CheckBoxApplyVertexMorphs As System.Windows.Forms.CheckBox
    Friend WithEvents StatusStrip1 As System.Windows.Forms.StatusStrip
    Friend WithEvents ToolStripStatusLabel1 As System.Windows.Forms.ToolStripStatusLabel
    Friend WithEvents ToolStripProgressBar1 As System.Windows.Forms.ToolStripProgressBar
End Class
