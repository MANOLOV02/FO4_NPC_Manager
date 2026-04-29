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
        ' === Containers ===
        Me.SplitContainer1 = New System.Windows.Forms.SplitContainer()
        Me.SplitContainerLeft = New System.Windows.Forms.SplitContainer()
        Me.SplitContainerPreview = New System.Windows.Forms.SplitContainer()
        Me.PanelNpcList = New System.Windows.Forms.Panel()
        Me.PanelRecordDetails = New System.Windows.Forms.Panel()
        Me.PanelPreviewControls = New System.Windows.Forms.Panel()
        Me.PanelPreviewHost = New System.Windows.Forms.Panel()
        ' === NPC list area ===
        Me.TreeViewNPCs = New System.Windows.Forms.TreeView()
        Me.TextBoxSearch = New System.Windows.Forms.TextBox()
        Me.LabelSearch = New System.Windows.Forms.Label()
        ' === Record details area ===
        Me.TreeViewRecordDetails = New System.Windows.Forms.TreeView()
        Me.LabelRecordTitle = New System.Windows.Forms.Label()
        ' === Preview toolbar ===
        Me.PanelPreviewToolbar = New System.Windows.Forms.TableLayoutPanel()
        Me.LabelPreviewMode = New System.Windows.Forms.Label()
        Me.ComboBoxPreviewMode = New System.Windows.Forms.ComboBox()
        Me.ComboBoxGender = New System.Windows.Forms.ComboBox()
        Me.ButtonRandomNPC = New System.Windows.Forms.Button()
        Me.LabelOutfit = New System.Windows.Forms.Label()
        Me.ComboBoxOutfit = New System.Windows.Forms.ComboBox()
        Me.ButtonReroll = New System.Windows.Forms.Button()
        Me.CheckBoxApplyBoneMorphs = New System.Windows.Forms.CheckBox()
        Me.CheckBoxApplyVertexMorphs = New System.Windows.Forms.CheckBox()
        Me.CheckBoxApplyBodyWeight = New System.Windows.Forms.CheckBox()
        Me.CheckBoxApplySculpt = New System.Windows.Forms.CheckBox()
        Me.CheckBoxRenderArmor = New System.Windows.Forms.CheckBox()
        Me.CheckBoxRenderUnderarmor = New System.Windows.Forms.CheckBox()
        Me.LabelStatus = New System.Windows.Forms.Label()
        ' === Status strip ===
        Me.StatusStrip1 = New System.Windows.Forms.StatusStrip()
        Me.ToolStripStatusLabel1 = New System.Windows.Forms.ToolStripStatusLabel()
        Me.ToolStripProgressBar1 = New System.Windows.Forms.ToolStripProgressBar()

        CType(Me.SplitContainer1, System.ComponentModel.ISupportInitialize).BeginInit()
        Me.SplitContainer1.Panel1.SuspendLayout()
        Me.SplitContainer1.Panel2.SuspendLayout()
        Me.SplitContainer1.SuspendLayout()
        CType(Me.SplitContainerLeft, System.ComponentModel.ISupportInitialize).BeginInit()
        Me.SplitContainerLeft.Panel1.SuspendLayout()
        Me.SplitContainerLeft.Panel2.SuspendLayout()
        Me.SplitContainerLeft.SuspendLayout()
        CType(Me.SplitContainerPreview, System.ComponentModel.ISupportInitialize).BeginInit()
        Me.SplitContainerPreview.Panel1.SuspendLayout()
        Me.SplitContainerPreview.Panel2.SuspendLayout()
        Me.SplitContainerPreview.SuspendLayout()
        Me.PanelNpcList.SuspendLayout()
        Me.PanelRecordDetails.SuspendLayout()
        Me.PanelPreviewControls.SuspendLayout()
        Me.PanelPreviewToolbar.SuspendLayout()
        Me.StatusStrip1.SuspendLayout()
        Me.SuspendLayout()
        '
        ' SplitContainer1 (vertical: left stack | right preview)
        '
        Me.SplitContainer1.Dock = System.Windows.Forms.DockStyle.Fill
        Me.SplitContainer1.Location = New System.Drawing.Point(0, 0)
        Me.SplitContainer1.Name = "SplitContainer1"
        Me.SplitContainer1.Orientation = System.Windows.Forms.Orientation.Vertical
        Me.SplitContainer1.Size = New System.Drawing.Size(1200, 778)
        Me.SplitContainer1.Panel1MinSize = 220
        Me.SplitContainer1.Panel2MinSize = 400
        Me.SplitContainer1.SplitterDistance = 280
        Me.SplitContainer1.Panel1.Controls.Add(Me.SplitContainerLeft)
        Me.SplitContainer1.Panel2.Controls.Add(Me.SplitContainerPreview)
        Me.SplitContainer1.TabIndex = 0
        '
        ' SplitContainerLeft (horizontal: top NPC list | bottom record details)
        '
        Me.SplitContainerLeft.Dock = System.Windows.Forms.DockStyle.Fill
        Me.SplitContainerLeft.Orientation = System.Windows.Forms.Orientation.Horizontal
        Me.SplitContainerLeft.Size = New System.Drawing.Size(280, 778)
        Me.SplitContainerLeft.Panel1MinSize = 150
        Me.SplitContainerLeft.Panel2MinSize = 150
        Me.SplitContainerLeft.SplitterDistance = 420
        Me.SplitContainerLeft.Panel1.Controls.Add(Me.PanelNpcList)
        Me.SplitContainerLeft.Panel2.Controls.Add(Me.PanelRecordDetails)
        '
        ' SplitContainerPreview (horizontal: top toolbar compact | bottom GL host)
        '
        Me.SplitContainerPreview.Dock = System.Windows.Forms.DockStyle.Fill
        Me.SplitContainerPreview.Orientation = System.Windows.Forms.Orientation.Horizontal
        Me.SplitContainerPreview.Size = New System.Drawing.Size(916, 778)
        Me.SplitContainerPreview.Panel1MinSize = 120
        Me.SplitContainerPreview.Panel2MinSize = 200
        Me.SplitContainerPreview.SplitterDistance = 160
        Me.SplitContainerPreview.FixedPanel = System.Windows.Forms.FixedPanel.Panel1
        Me.SplitContainerPreview.Panel1.Controls.Add(Me.PanelPreviewControls)
        Me.SplitContainerPreview.Panel2.Controls.Add(Me.PanelPreviewHost)
        '
        ' PanelNpcList
        '
        Me.PanelNpcList.Controls.Add(Me.TreeViewNPCs)
        Me.PanelNpcList.Controls.Add(Me.TextBoxSearch)
        Me.PanelNpcList.Controls.Add(Me.LabelSearch)
        Me.PanelNpcList.Dock = System.Windows.Forms.DockStyle.Fill
        Me.PanelNpcList.Name = "PanelNpcList"
        Me.PanelNpcList.Padding = New System.Windows.Forms.Padding(6, 6, 6, 6)
        '
        ' LabelSearch
        '
        Me.LabelSearch.AutoSize = True
        Me.LabelSearch.Location = New System.Drawing.Point(8, 10)
        Me.LabelSearch.Name = "LabelSearch"
        Me.LabelSearch.Text = "Search:"
        '
        ' TextBoxSearch
        '
        Me.TextBoxSearch.Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right
        Me.TextBoxSearch.Location = New System.Drawing.Point(60, 7)
        Me.TextBoxSearch.Size = New System.Drawing.Size(212, 23)
        Me.TextBoxSearch.PlaceholderText = "Filter NPCs..."
        Me.TextBoxSearch.Name = "TextBoxSearch"
        '
        ' TreeViewNPCs
        '
        Me.TreeViewNPCs.Anchor = AnchorStyles.Top Or AnchorStyles.Bottom Or AnchorStyles.Left Or AnchorStyles.Right
        Me.TreeViewNPCs.Location = New System.Drawing.Point(8, 38)
        Me.TreeViewNPCs.Size = New System.Drawing.Size(264, 374)
        Me.TreeViewNPCs.Name = "TreeViewNPCs"
        Me.TreeViewNPCs.DrawMode = System.Windows.Forms.TreeViewDrawMode.OwnerDrawText
        Me.TreeViewNPCs.HideSelection = False
        Me.TreeViewNPCs.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle
        '
        ' PanelRecordDetails
        '
        Me.PanelRecordDetails.Controls.Add(Me.TreeViewRecordDetails)
        Me.PanelRecordDetails.Controls.Add(Me.LabelRecordTitle)
        Me.PanelRecordDetails.Dock = System.Windows.Forms.DockStyle.Fill
        Me.PanelRecordDetails.Name = "PanelRecordDetails"
        '
        ' LabelRecordTitle
        '
        Me.LabelRecordTitle.Dock = System.Windows.Forms.DockStyle.Top
        Me.LabelRecordTitle.Font = New System.Drawing.Font("Segoe UI Semibold", 9.75!, System.Drawing.FontStyle.Bold)
        Me.LabelRecordTitle.Name = "LabelRecordTitle"
        Me.LabelRecordTitle.Size = New System.Drawing.Size(280, 24)
        Me.LabelRecordTitle.Text = "  Record Details"
        Me.LabelRecordTitle.TextAlign = System.Drawing.ContentAlignment.MiddleLeft
        Me.LabelRecordTitle.BackColor = System.Drawing.SystemColors.ControlDark
        Me.LabelRecordTitle.ForeColor = System.Drawing.SystemColors.ControlLightLight
        '
        ' TreeViewRecordDetails
        '
        Me.TreeViewRecordDetails.Dock = System.Windows.Forms.DockStyle.Fill
        Me.TreeViewRecordDetails.Name = "TreeViewRecordDetails"
        Me.TreeViewRecordDetails.Font = New System.Drawing.Font("Cascadia Code", 8.5!, System.Drawing.FontStyle.Regular)
        Me.TreeViewRecordDetails.BorderStyle = System.Windows.Forms.BorderStyle.None
        '
        ' PanelPreviewControls (toolbar host)
        '
        Me.PanelPreviewControls.Controls.Add(Me.PanelPreviewToolbar)
        Me.PanelPreviewControls.Controls.Add(Me.LabelStatus)
        Me.PanelPreviewControls.Dock = System.Windows.Forms.DockStyle.Fill
        Me.PanelPreviewControls.Padding = New System.Windows.Forms.Padding(8, 6, 8, 6)
        Me.PanelPreviewControls.Name = "PanelPreviewControls"
        '
        ' PanelPreviewHost (GLControl host — exclusivo)
        '
        Me.PanelPreviewHost.Dock = System.Windows.Forms.DockStyle.Fill
        Me.PanelPreviewHost.Name = "PanelPreviewHost"
        Me.PanelPreviewHost.BackColor = System.Drawing.Color.FromArgb(40, 40, 44)
        '
        ' PanelPreviewToolbar (TableLayoutPanel 4 cols x 6 rows compact, 3-col check grid)
        ' Layout:
        '   Row 0: [Preview:] [comboMode]    [comboGender] [btnRandom]
        '   Row 1: [Outfit:]  [comboOutfit (col 1+2 span)] [btnReroll]
        '   Row 2: [chkBone]  [chkRenderArmor]              [chkRenderUnderarmor]
        '   Row 3: [chkVertex]
        '   Row 4: [chkBodyWeight]
        '   Row 5: [chkSculpt]
        ' Col 0 = morphs (AutoSize), col 1 = render-armor (50%), col 2 = render-underarmor (50%),
        ' col 3 = AutoSize buttons. Los checks de render ocupan una sola fila cada uno.
        '
        Me.PanelPreviewToolbar.Dock = System.Windows.Forms.DockStyle.Top
        Me.PanelPreviewToolbar.AutoSize = True
        Me.PanelPreviewToolbar.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink
        Me.PanelPreviewToolbar.Name = "PanelPreviewToolbar"
        Me.PanelPreviewToolbar.ColumnCount = 4
        Me.PanelPreviewToolbar.RowCount = 6
        Me.PanelPreviewToolbar.ColumnStyles.Add(New System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.AutoSize))
        Me.PanelPreviewToolbar.ColumnStyles.Add(New System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50.0!))
        Me.PanelPreviewToolbar.ColumnStyles.Add(New System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50.0!))
        Me.PanelPreviewToolbar.ColumnStyles.Add(New System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.AutoSize))
        Me.PanelPreviewToolbar.RowStyles.Add(New System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize))
        Me.PanelPreviewToolbar.RowStyles.Add(New System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize))
        Me.PanelPreviewToolbar.RowStyles.Add(New System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize))
        Me.PanelPreviewToolbar.RowStyles.Add(New System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize))
        Me.PanelPreviewToolbar.RowStyles.Add(New System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize))
        Me.PanelPreviewToolbar.RowStyles.Add(New System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize))
        ' Row 0 — preview mode + gender + random button
        Me.PanelPreviewToolbar.Controls.Add(Me.LabelPreviewMode, 0, 0)
        Me.PanelPreviewToolbar.Controls.Add(Me.ComboBoxPreviewMode, 1, 0)
        Me.PanelPreviewToolbar.Controls.Add(Me.ComboBoxGender, 2, 0)
        Me.PanelPreviewToolbar.Controls.Add(Me.ButtonRandomNPC, 3, 0)
        ' Row 1 — outfit selector + reroll button
        Me.PanelPreviewToolbar.Controls.Add(Me.LabelOutfit, 0, 1)
        Me.PanelPreviewToolbar.SetColumnSpan(Me.ComboBoxOutfit, 2)
        Me.PanelPreviewToolbar.Controls.Add(Me.ComboBoxOutfit, 1, 1)
        Me.PanelPreviewToolbar.Controls.Add(Me.ButtonReroll, 3, 1)
        ' Row 2 — first morph + both render toggles (one per col)
        Me.PanelPreviewToolbar.Controls.Add(Me.CheckBoxApplyBoneMorphs, 0, 2)
        Me.PanelPreviewToolbar.Controls.Add(Me.CheckBoxRenderArmor, 1, 2)
        Me.PanelPreviewToolbar.Controls.Add(Me.CheckBoxRenderUnderarmor, 2, 2)
        ' Row 3-5 — remaining morphs (col 0 only)
        Me.PanelPreviewToolbar.Controls.Add(Me.CheckBoxApplyVertexMorphs, 0, 3)
        Me.PanelPreviewToolbar.Controls.Add(Me.CheckBoxApplyBodyWeight, 0, 4)
        Me.PanelPreviewToolbar.Controls.Add(Me.CheckBoxApplySculpt, 0, 5)
        '
        ' LabelPreviewMode
        '
        Me.LabelPreviewMode.AutoSize = True
        Me.LabelPreviewMode.Anchor = AnchorStyles.Left
        Me.LabelPreviewMode.Margin = New System.Windows.Forms.Padding(2, 6, 4, 0)
        Me.LabelPreviewMode.Name = "LabelPreviewMode"
        Me.LabelPreviewMode.Text = "Preview:"
        '
        ' ComboBoxPreviewMode
        '
        Me.ComboBoxPreviewMode.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList
        Me.ComboBoxPreviewMode.Items.AddRange(New Object() {"Full Character", "Only Face"})
        Me.ComboBoxPreviewMode.Name = "ComboBoxPreviewMode"
        Me.ComboBoxPreviewMode.Dock = System.Windows.Forms.DockStyle.Fill
        Me.ComboBoxPreviewMode.Margin = New System.Windows.Forms.Padding(2, 2, 4, 2)
        Me.ComboBoxPreviewMode.SelectedIndex = 0
        '
        ' ComboBoxGender
        '
        Me.ComboBoxGender.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList
        Me.ComboBoxGender.Items.AddRange(New Object() {"Random", "Male", "Female"})
        Me.ComboBoxGender.Name = "ComboBoxGender"
        Me.ComboBoxGender.Dock = System.Windows.Forms.DockStyle.Fill
        Me.ComboBoxGender.Margin = New System.Windows.Forms.Padding(2, 2, 4, 2)
        Me.ComboBoxGender.SelectedIndex = 0
        '
        ' ButtonRandomNPC
        '
        Me.ButtonRandomNPC.Text = "🎲"
        Me.ButtonRandomNPC.Font = New System.Drawing.Font("Segoe UI Symbol", 10.0!, System.Drawing.FontStyle.Bold)
        Me.ButtonRandomNPC.Size = New System.Drawing.Size(32, 26)
        Me.ButtonRandomNPC.FlatStyle = System.Windows.Forms.FlatStyle.System
        Me.ButtonRandomNPC.Margin = New System.Windows.Forms.Padding(2)
        '
        ' LabelOutfit
        '
        Me.LabelOutfit.AutoSize = True
        Me.LabelOutfit.Anchor = AnchorStyles.Left
        Me.LabelOutfit.Margin = New System.Windows.Forms.Padding(2, 6, 4, 0)
        Me.LabelOutfit.Name = "LabelOutfit"
        Me.LabelOutfit.Text = "Outfit:"
        '
        ' ComboBoxOutfit
        '
        Me.ComboBoxOutfit.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList
        Me.ComboBoxOutfit.Dock = System.Windows.Forms.DockStyle.Fill
        Me.ComboBoxOutfit.Margin = New System.Windows.Forms.Padding(2, 2, 4, 2)
        Me.ComboBoxOutfit.Name = "ComboBoxOutfit"
        '
        ' ButtonReroll
        '
        Me.ButtonReroll.Text = "↻"
        Me.ButtonReroll.Font = New System.Drawing.Font("Segoe UI Symbol", 10.0!, System.Drawing.FontStyle.Bold)
        Me.ButtonReroll.Size = New System.Drawing.Size(32, 26)
        Me.ButtonReroll.FlatStyle = System.Windows.Forms.FlatStyle.System
        Me.ButtonReroll.Margin = New System.Windows.Forms.Padding(2)
        '
        ' Checkboxes (compact layout)
        '
        Me.CheckBoxApplyBoneMorphs.AutoSize = True
        Me.CheckBoxApplyBoneMorphs.Checked = True
        Me.CheckBoxApplyBoneMorphs.CheckState = System.Windows.Forms.CheckState.Checked
        Me.CheckBoxApplyBoneMorphs.Name = "CheckBoxApplyBoneMorphs"
        Me.CheckBoxApplyBoneMorphs.Text = "Bone morphs (FMRS)"
        Me.CheckBoxApplyBoneMorphs.Margin = New System.Windows.Forms.Padding(2, 1, 8, 1)

        Me.CheckBoxApplyVertexMorphs.AutoSize = True
        Me.CheckBoxApplyVertexMorphs.Checked = True
        Me.CheckBoxApplyVertexMorphs.CheckState = System.Windows.Forms.CheckState.Checked
        Me.CheckBoxApplyVertexMorphs.Name = "CheckBoxApplyVertexMorphs"
        Me.CheckBoxApplyVertexMorphs.Text = "Vertex morphs (TRI)"
        Me.CheckBoxApplyVertexMorphs.Margin = New System.Windows.Forms.Padding(2, 1, 8, 1)

        Me.CheckBoxApplyBodyWeight.AutoSize = True
        Me.CheckBoxApplyBodyWeight.Checked = True
        Me.CheckBoxApplyBodyWeight.CheckState = System.Windows.Forms.CheckState.Checked
        Me.CheckBoxApplyBodyWeight.Name = "CheckBoxApplyBodyWeight"
        Me.CheckBoxApplyBodyWeight.Text = "Body weight (MWGT)"
        Me.CheckBoxApplyBodyWeight.Margin = New System.Windows.Forms.Padding(2, 1, 8, 1)

        Me.CheckBoxApplySculpt.AutoSize = True
        Me.CheckBoxApplySculpt.Checked = True
        Me.CheckBoxApplySculpt.CheckState = System.Windows.Forms.CheckState.Checked
        Me.CheckBoxApplySculpt.Name = "CheckBoxApplySculpt"
        Me.CheckBoxApplySculpt.Text = "Sculpt (ARMA SCLP)"
        Me.CheckBoxApplySculpt.Margin = New System.Windows.Forms.Padding(2, 1, 8, 1)

        Me.CheckBoxRenderArmor.AutoSize = True
        Me.CheckBoxRenderArmor.Checked = True
        Me.CheckBoxRenderArmor.CheckState = System.Windows.Forms.CheckState.Checked
        Me.CheckBoxRenderArmor.Name = "CheckBoxRenderArmor"
        Me.CheckBoxRenderArmor.Text = "Render armor [A]"
        Me.CheckBoxRenderArmor.Margin = New System.Windows.Forms.Padding(2, 1, 8, 1)

        Me.CheckBoxRenderUnderarmor.AutoSize = True
        Me.CheckBoxRenderUnderarmor.Checked = True
        Me.CheckBoxRenderUnderarmor.CheckState = System.Windows.Forms.CheckState.Checked
        Me.CheckBoxRenderUnderarmor.Name = "CheckBoxRenderUnderarmor"
        Me.CheckBoxRenderUnderarmor.Text = "Render underarmor"
        Me.CheckBoxRenderUnderarmor.Margin = New System.Windows.Forms.Padding(2, 1, 8, 1)
        '
        ' LabelStatus (loading placeholder, occupies remainder of toolbar host)
        '
        Me.LabelStatus.Dock = System.Windows.Forms.DockStyle.Fill
        Me.LabelStatus.Font = New System.Drawing.Font("Segoe UI", 11.0!, System.Drawing.FontStyle.Regular)
        Me.LabelStatus.Name = "LabelStatus"
        Me.LabelStatus.Text = "Loading..."
        Me.LabelStatus.TextAlign = System.Drawing.ContentAlignment.MiddleCenter
        Me.LabelStatus.ForeColor = System.Drawing.SystemColors.GrayText
        '
        ' StatusStrip1
        '
        Me.StatusStrip1.Items.AddRange(New System.Windows.Forms.ToolStripItem() {Me.ToolStripStatusLabel1, Me.ToolStripProgressBar1})
        Me.StatusStrip1.Location = New System.Drawing.Point(0, 778)
        Me.StatusStrip1.Name = "StatusStrip1"
        Me.StatusStrip1.Size = New System.Drawing.Size(1200, 22)

        Me.ToolStripStatusLabel1.Name = "ToolStripStatusLabel1"
        Me.ToolStripStatusLabel1.Size = New System.Drawing.Size(1083, 17)
        Me.ToolStripStatusLabel1.Spring = True
        Me.ToolStripStatusLabel1.TextAlign = System.Drawing.ContentAlignment.MiddleLeft
        Me.ToolStripStatusLabel1.Text = "Ready"

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

        Me.PanelPreviewToolbar.ResumeLayout(False)
        Me.PanelPreviewToolbar.PerformLayout()
        Me.PanelPreviewControls.ResumeLayout(False)
        Me.PanelPreviewControls.PerformLayout()
        Me.PanelRecordDetails.ResumeLayout(False)
        Me.PanelNpcList.ResumeLayout(False)
        Me.PanelNpcList.PerformLayout()
        Me.SplitContainerPreview.Panel1.ResumeLayout(False)
        Me.SplitContainerPreview.Panel2.ResumeLayout(False)
        Me.SplitContainerPreview.ResumeLayout(False)
        CType(Me.SplitContainerPreview, System.ComponentModel.ISupportInitialize).EndInit()
        Me.SplitContainerLeft.Panel1.ResumeLayout(False)
        Me.SplitContainerLeft.Panel2.ResumeLayout(False)
        Me.SplitContainerLeft.ResumeLayout(False)
        CType(Me.SplitContainerLeft, System.ComponentModel.ISupportInitialize).EndInit()
        Me.SplitContainer1.Panel1.ResumeLayout(False)
        Me.SplitContainer1.Panel2.ResumeLayout(False)
        Me.SplitContainer1.ResumeLayout(False)
        CType(Me.SplitContainer1, System.ComponentModel.ISupportInitialize).EndInit()
        Me.StatusStrip1.ResumeLayout(False)
        Me.StatusStrip1.PerformLayout()
        Me.ResumeLayout(False)
        Me.PerformLayout()
    End Sub

    Friend WithEvents SplitContainer1 As System.Windows.Forms.SplitContainer
    Friend WithEvents SplitContainerLeft As System.Windows.Forms.SplitContainer
    Friend WithEvents SplitContainerPreview As System.Windows.Forms.SplitContainer
    Friend WithEvents PanelNpcList As System.Windows.Forms.Panel
    Friend WithEvents PanelRecordDetails As System.Windows.Forms.Panel
    Friend WithEvents PanelPreviewControls As System.Windows.Forms.Panel
    Friend WithEvents PanelPreviewHost As System.Windows.Forms.Panel
    Friend WithEvents TreeViewNPCs As System.Windows.Forms.TreeView
    Friend WithEvents TextBoxSearch As System.Windows.Forms.TextBox
    Friend WithEvents LabelSearch As System.Windows.Forms.Label
    Friend WithEvents TreeViewRecordDetails As System.Windows.Forms.TreeView
    Friend WithEvents LabelRecordTitle As System.Windows.Forms.Label
    Friend WithEvents PanelPreviewToolbar As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents LabelPreviewMode As System.Windows.Forms.Label
    Friend WithEvents ComboBoxPreviewMode As System.Windows.Forms.ComboBox
    Friend WithEvents ComboBoxGender As System.Windows.Forms.ComboBox
    Friend WithEvents ButtonRandomNPC As System.Windows.Forms.Button
    Friend WithEvents LabelOutfit As System.Windows.Forms.Label
    Friend WithEvents ComboBoxOutfit As System.Windows.Forms.ComboBox
    Friend WithEvents ButtonReroll As System.Windows.Forms.Button
    Friend WithEvents CheckBoxApplyBoneMorphs As System.Windows.Forms.CheckBox
    Friend WithEvents CheckBoxApplyVertexMorphs As System.Windows.Forms.CheckBox
    Friend WithEvents CheckBoxApplyBodyWeight As System.Windows.Forms.CheckBox
    Friend WithEvents CheckBoxApplySculpt As System.Windows.Forms.CheckBox
    Friend WithEvents CheckBoxRenderArmor As System.Windows.Forms.CheckBox
    Friend WithEvents CheckBoxRenderUnderarmor As System.Windows.Forms.CheckBox
    Friend WithEvents LabelStatus As System.Windows.Forms.Label
    Friend WithEvents StatusStrip1 As System.Windows.Forms.StatusStrip
    Friend WithEvents ToolStripStatusLabel1 As System.Windows.Forms.ToolStripStatusLabel
    Friend WithEvents ToolStripProgressBar1 As System.Windows.Forms.ToolStripProgressBar
End Class
