' UI built in Designer per feedback_ui_in_designer.md.
<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class EditBody_Form
    Inherits System.Windows.Forms.Form

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
        PreviewSplit = New System.Windows.Forms.SplitContainer()
        PreviewSidebar = New System.Windows.Forms.TableLayoutPanel()
        RenderTogglesPanel = New System.Windows.Forms.FlowLayoutPanel()
        CheckBoxRenderUnderarmor = New System.Windows.Forms.CheckBox()
        CheckBoxRenderArmor = New System.Windows.Forms.CheckBox()
        CheckBoxRenderHeadwear = New System.Windows.Forms.CheckBox()
        CheckBoxRenderGore = New System.Windows.Forms.CheckBox()
        PreviewHostPanel = New System.Windows.Forms.Panel()
        RootLayout = New System.Windows.Forms.TableLayoutPanel()
        GroupBoxWeight = New System.Windows.Forms.GroupBox()
        WeightLayout = New System.Windows.Forms.TableLayoutPanel()
        WeightTriangle = New WeightTriangleControl()
        WeightLegend = New System.Windows.Forms.TableLayoutPanel()
        LabelMuscular = New System.Windows.Forms.Label()
        SliderMuscular = New FO4_Base_Library.TinySliderTextBox()
        LabelThin = New System.Windows.Forms.Label()
        SliderThin = New FO4_Base_Library.TinySliderTextBox()
        LabelFat = New System.Windows.Forms.Label()
        SliderFat = New FO4_Base_Library.TinySliderTextBox()

        GroupBoxMrsv = New System.Windows.Forms.GroupBox()
        MrsvLayout = New System.Windows.Forms.TableLayoutPanel()

        GroupBoxBodySlide = New System.Windows.Forms.GroupBox()
        BodySlideLayout = New System.Windows.Forms.TableLayoutPanel()
        TextBoxBodySlideFilter = New System.Windows.Forms.TextBox()
        BodySlidePanel = New System.Windows.Forms.FlowLayoutPanel()

        ButtonOk = New System.Windows.Forms.Button()
        ButtonCancel = New System.Windows.Forms.Button()
        ButtonResetSection = New System.Windows.Forms.Button()
        BottomLayout = New System.Windows.Forms.FlowLayoutPanel()

        CType(PreviewSplit, System.ComponentModel.ISupportInitialize).BeginInit()
        PreviewSplit.Panel1.SuspendLayout()
        PreviewSplit.Panel2.SuspendLayout()
        PreviewSplit.SuspendLayout()
        RootLayout.SuspendLayout()
        GroupBoxWeight.SuspendLayout()
        WeightLayout.SuspendLayout()
        WeightLegend.SuspendLayout()
        GroupBoxMrsv.SuspendLayout()
        GroupBoxBodySlide.SuspendLayout()
        BodySlideLayout.SuspendLayout()
        BottomLayout.SuspendLayout()
        SuspendLayout()
        '
        ' PreviewSplit
        '
        ' Splits the form into two panels: Panel1 hosts the editor controls (the RootLayout that
        ' was previously docked to the form root), Panel2 hosts the preview (a PreviewControl
        ' created at Form.Shown — see WM Editor_Form/CreatefromNif_Form for the canonical pattern).
        ' FixedPanel=Panel2 keeps the preview width stable when the form is resized so the editor
        ' on the left gets the extra width.
        PreviewSplit.Dock = System.Windows.Forms.DockStyle.Fill
        PreviewSplit.FixedPanel = System.Windows.Forms.FixedPanel.Panel2
        PreviewSplit.Location = New System.Drawing.Point(0, 0)
        PreviewSplit.Name = "PreviewSplit"
        PreviewSplit.Panel1.Controls.Add(RootLayout)
        PreviewSplit.Panel2.Controls.Add(PreviewSidebar)
        PreviewSplit.Size = New System.Drawing.Size(940, 640)
        PreviewSplit.SplitterDistance = 560
        PreviewSplit.TabIndex = 0
        '
        ' PreviewSidebar — vertical TableLayoutPanel: row 0 hosts the per-editor render toggles
        ' (Underarmor / Armor / Headwear / Gore), row 1 hosts the PreviewControl. Row 0 AutoSize
        ' so the toggle bar is exactly the height of one CheckBox row; row 1 fills the rest.
        '
        PreviewSidebar.Dock = System.Windows.Forms.DockStyle.Fill
        PreviewSidebar.ColumnCount = 1
        PreviewSidebar.RowCount = 2
        PreviewSidebar.ColumnStyles.Add(New System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100.0F))
        PreviewSidebar.RowStyles.Add(New System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize))
        PreviewSidebar.RowStyles.Add(New System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100.0F))
        PreviewSidebar.Controls.Add(RenderTogglesPanel, 0, 0)
        PreviewSidebar.Controls.Add(PreviewHostPanel, 0, 1)
        '
        ' RenderTogglesPanel
        '
        RenderTogglesPanel.Dock = System.Windows.Forms.DockStyle.Fill
        RenderTogglesPanel.AutoSize = True
        RenderTogglesPanel.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink
        RenderTogglesPanel.FlowDirection = System.Windows.Forms.FlowDirection.LeftToRight
        RenderTogglesPanel.WrapContents = True
        RenderTogglesPanel.Padding = New System.Windows.Forms.Padding(2)
        RenderTogglesPanel.Controls.Add(CheckBoxRenderUnderarmor)
        RenderTogglesPanel.Controls.Add(CheckBoxRenderArmor)
        RenderTogglesPanel.Controls.Add(CheckBoxRenderHeadwear)
        RenderTogglesPanel.Controls.Add(CheckBoxRenderGore)
        '
        CheckBoxRenderUnderarmor.AutoSize = True
        CheckBoxRenderUnderarmor.Name = "CheckBoxRenderUnderarmor"
        CheckBoxRenderUnderarmor.Text = "Render underarmor"
        CheckBoxRenderUnderarmor.Margin = New System.Windows.Forms.Padding(4, 2, 8, 2)
        '
        CheckBoxRenderArmor.AutoSize = True
        CheckBoxRenderArmor.Name = "CheckBoxRenderArmor"
        CheckBoxRenderArmor.Text = "Render armor"
        CheckBoxRenderArmor.Margin = New System.Windows.Forms.Padding(4, 2, 8, 2)
        '
        CheckBoxRenderHeadwear.AutoSize = True
        CheckBoxRenderHeadwear.Name = "CheckBoxRenderHeadwear"
        CheckBoxRenderHeadwear.Text = "Render headwear"
        CheckBoxRenderHeadwear.Margin = New System.Windows.Forms.Padding(4, 2, 8, 2)
        '
        CheckBoxRenderGore.AutoSize = True
        CheckBoxRenderGore.Name = "CheckBoxRenderGore"
        CheckBoxRenderGore.Text = "Render gore"
        CheckBoxRenderGore.Margin = New System.Windows.Forms.Padding(4, 2, 8, 2)
        '
        ' PreviewHostPanel
        '
        ' Empty in Designer; the actual PreviewControl is added at Form.Shown so the GL context
        ' isn't constructed until the form is visible. Disposed in FormClosing.
        PreviewHostPanel.Dock = System.Windows.Forms.DockStyle.Fill
        PreviewHostPanel.Location = New System.Drawing.Point(0, 0)
        PreviewHostPanel.Name = "PreviewHostPanel"
        PreviewHostPanel.Size = New System.Drawing.Size(380, 600)
        PreviewHostPanel.TabIndex = 0
        '
        ' RootLayout
        '
        RootLayout.Dock = System.Windows.Forms.DockStyle.Fill
        RootLayout.ColumnCount = 1
        RootLayout.ColumnStyles.Add(New System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100.0F))
        RootLayout.RowCount = 4
        RootLayout.RowStyles.Add(New System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize))
        RootLayout.RowStyles.Add(New System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize))
        RootLayout.RowStyles.Add(New System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100.0F))
        RootLayout.RowStyles.Add(New System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize))
        RootLayout.Controls.Add(GroupBoxWeight, 0, 0)
        RootLayout.Controls.Add(GroupBoxMrsv, 0, 1)
        RootLayout.Controls.Add(GroupBoxBodySlide, 0, 2)
        RootLayout.Controls.Add(BottomLayout, 0, 3)
        RootLayout.Padding = New System.Windows.Forms.Padding(8)
        '
        ' GroupBoxWeight
        '
        GroupBoxWeight.Text = "Weight (NPC.MWGT — applied via bone scaling)"
        GroupBoxWeight.Dock = System.Windows.Forms.DockStyle.Fill
        GroupBoxWeight.AutoSize = True
        GroupBoxWeight.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink
        GroupBoxWeight.Controls.Add(WeightLayout)
        '
        ' WeightLayout — barycentric triangle (left) + read-only legend (right). The triple is
        ' inherently constrained to sum=1 by the triangle's geometry; we do not show three
        ' independent sliders because that would invite drift the engine doesn't allow.
        '
        WeightLayout.Dock = System.Windows.Forms.DockStyle.Fill
        WeightLayout.ColumnCount = 2
        WeightLayout.ColumnStyles.Add(New System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100.0F))
        WeightLayout.ColumnStyles.Add(New System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.AutoSize))
        WeightLayout.RowCount = 1
        WeightLayout.RowStyles.Add(New System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize))
        WeightLayout.AutoSize = True
        WeightLayout.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink
        WeightLayout.Padding = New System.Windows.Forms.Padding(4)
        '
        ' WeightTriangle — primary input. Drag inside to set (Thin, Muscular, Fat).
        '
        WeightTriangle.Dock = System.Windows.Forms.DockStyle.Fill
        WeightTriangle.MinimumSize = New System.Drawing.Size(220, 180)
        WeightTriangle.Margin = New System.Windows.Forms.Padding(2)
        WeightLayout.Controls.Add(WeightTriangle, 0, 0)
        '
        ' WeightLegend — three read-only rows showing current values.
        '
        WeightLegend.AutoSize = True
        WeightLegend.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink
        WeightLegend.ColumnCount = 2
        WeightLegend.ColumnStyles.Add(New System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.AutoSize))
        WeightLegend.ColumnStyles.Add(New System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.AutoSize))
        WeightLegend.RowCount = 3
        WeightLegend.RowStyles.Add(New System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize))
        WeightLegend.RowStyles.Add(New System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize))
        WeightLegend.RowStyles.Add(New System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize))
        WeightLegend.Margin = New System.Windows.Forms.Padding(8, 2, 2, 2)
        WeightLegend.Anchor = System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Right
        '
        LabelMuscular.Text = "Muscular:"
        LabelMuscular.AutoSize = True
        LabelMuscular.TextAlign = System.Drawing.ContentAlignment.MiddleLeft
        SliderMuscular.Minimum = 0R
        SliderMuscular.Maximum = 1R
        SliderMuscular.SmallChange = 0.01R
        SliderMuscular.LargeChange = 0.1R
        SliderMuscular.DisplayFormat = "0.00"
        SliderMuscular.MinimumSize = New System.Drawing.Size(140, 22)
        SliderMuscular.Size = New System.Drawing.Size(160, 22)
        SliderMuscular.Margin = New System.Windows.Forms.Padding(2)
        '
        LabelThin.Text = "Thin:"
        LabelThin.AutoSize = True
        LabelThin.TextAlign = System.Drawing.ContentAlignment.MiddleLeft
        SliderThin.Minimum = 0R
        SliderThin.Maximum = 1R
        SliderThin.SmallChange = 0.01R
        SliderThin.LargeChange = 0.1R
        SliderThin.DisplayFormat = "0.00"
        SliderThin.MinimumSize = New System.Drawing.Size(140, 22)
        SliderThin.Size = New System.Drawing.Size(160, 22)
        SliderThin.Margin = New System.Windows.Forms.Padding(2)
        '
        LabelFat.Text = "Fat:"
        LabelFat.AutoSize = True
        LabelFat.TextAlign = System.Drawing.ContentAlignment.MiddleLeft
        SliderFat.Minimum = 0R
        SliderFat.Maximum = 1R
        SliderFat.SmallChange = 0.01R
        SliderFat.LargeChange = 0.1R
        SliderFat.DisplayFormat = "0.00"
        SliderFat.MinimumSize = New System.Drawing.Size(140, 22)
        SliderFat.Size = New System.Drawing.Size(160, 22)
        SliderFat.Margin = New System.Windows.Forms.Padding(2)
        '
        ' Legend layout: Muscular at top (matches triangle apex), then Thin (bottom-left vertex),
        ' then Fat (bottom-right vertex). Mirrors the triangle's spatial arrangement so the
        ' user can read top→bottom and orient themselves.
        WeightLegend.Controls.Add(LabelMuscular, 0, 0)
        WeightLegend.Controls.Add(SliderMuscular, 1, 0)
        WeightLegend.Controls.Add(LabelThin, 0, 1)
        WeightLegend.Controls.Add(SliderThin, 1, 1)
        WeightLegend.Controls.Add(LabelFat, 0, 2)
        WeightLegend.Controls.Add(SliderFat, 1, 2)
        WeightLayout.Controls.Add(WeightLegend, 1, 0)
        '
        ' GroupBoxMrsv
        '
        GroupBoxMrsv.Text = "Body Morph Regions (NPC.MRSV — vanilla 5 regions, applied via bone scaling)"
        GroupBoxMrsv.Dock = System.Windows.Forms.DockStyle.Fill
        GroupBoxMrsv.AutoSize = True
        GroupBoxMrsv.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink
        GroupBoxMrsv.Controls.Add(MrsvLayout)
        '
        ' MrsvLayout — populated dynamically with 5 (Label, slider) rows.
        '
        MrsvLayout.Dock = System.Windows.Forms.DockStyle.Fill
        MrsvLayout.ColumnCount = 2
        MrsvLayout.ColumnStyles.Add(New System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.AutoSize))
        MrsvLayout.ColumnStyles.Add(New System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100.0F))
        MrsvLayout.RowCount = 5
        MrsvLayout.RowStyles.Add(New System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize))
        MrsvLayout.RowStyles.Add(New System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize))
        MrsvLayout.RowStyles.Add(New System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize))
        MrsvLayout.RowStyles.Add(New System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize))
        MrsvLayout.RowStyles.Add(New System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize))
        MrsvLayout.AutoSize = True
        MrsvLayout.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink
        MrsvLayout.Padding = New System.Windows.Forms.Padding(4)
        '
        ' GroupBoxBodySlide
        '
        GroupBoxBodySlide.Text = "BodySlide Sliders (PIRT .tri — vertex morphs, F4SE-only field)"
        GroupBoxBodySlide.Dock = System.Windows.Forms.DockStyle.Fill
        GroupBoxBodySlide.Controls.Add(BodySlideLayout)
        '
        ' BodySlideLayout
        '
        BodySlideLayout.Dock = System.Windows.Forms.DockStyle.Fill
        BodySlideLayout.ColumnCount = 1
        BodySlideLayout.ColumnStyles.Add(New System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100.0F))
        BodySlideLayout.RowCount = 2
        BodySlideLayout.RowStyles.Add(New System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize))
        BodySlideLayout.RowStyles.Add(New System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100.0F))
        BodySlideLayout.Padding = New System.Windows.Forms.Padding(4)
        '
        ' TextBoxBodySlideFilter
        '
        TextBoxBodySlideFilter.PlaceholderText = "Filter sliders…"
        TextBoxBodySlideFilter.Dock = System.Windows.Forms.DockStyle.Top
        BodySlideLayout.Controls.Add(TextBoxBodySlideFilter, 0, 0)
        '
        ' BodySlidePanel — populated dynamically. Auto-scroll handles N >> visible.
        '
        BodySlidePanel.Dock = System.Windows.Forms.DockStyle.Fill
        BodySlidePanel.AutoScroll = True
        BodySlidePanel.FlowDirection = System.Windows.Forms.FlowDirection.TopDown
        BodySlidePanel.WrapContents = False
        BodySlideLayout.Controls.Add(BodySlidePanel, 0, 1)
        '
        ' BottomLayout
        '
        BottomLayout.Dock = System.Windows.Forms.DockStyle.Fill
        BottomLayout.FlowDirection = System.Windows.Forms.FlowDirection.RightToLeft
        BottomLayout.AutoSize = True
        BottomLayout.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink
        BottomLayout.Padding = New System.Windows.Forms.Padding(0, 4, 0, 0)
        ButtonOk.Text = "OK"
        ButtonOk.Width = 80
        ButtonCancel.Text = "Cancel"
        ButtonCancel.Width = 80
        ButtonResetSection.Text = "Reset BodySlide"
        ButtonResetSection.Width = 110
        BottomLayout.Controls.Add(ButtonOk)
        BottomLayout.Controls.Add(ButtonCancel)
        BottomLayout.Controls.Add(ButtonResetSection)
        '
        ' EditBody_Form
        '
        AcceptButton = ButtonOk
        CancelButton = ButtonCancel
        Text = "Edit Body"
        ClientSize = New System.Drawing.Size(940, 640)
        StartPosition = System.Windows.Forms.FormStartPosition.CenterParent
        Controls.Add(PreviewSplit)

        RootLayout.ResumeLayout(False)
        RootLayout.PerformLayout()
        GroupBoxWeight.ResumeLayout(False)
        GroupBoxWeight.PerformLayout()
        WeightLayout.ResumeLayout(False)
        WeightLayout.PerformLayout()
        WeightLegend.ResumeLayout(False)
        WeightLegend.PerformLayout()
        GroupBoxMrsv.ResumeLayout(False)
        GroupBoxMrsv.PerformLayout()
        GroupBoxBodySlide.ResumeLayout(False)
        BodySlideLayout.ResumeLayout(False)
        BodySlideLayout.PerformLayout()
        BottomLayout.ResumeLayout(False)
        BottomLayout.PerformLayout()
        PreviewSplit.Panel1.ResumeLayout(False)
        PreviewSplit.Panel2.ResumeLayout(False)
        CType(PreviewSplit, System.ComponentModel.ISupportInitialize).EndInit()
        PreviewSplit.ResumeLayout(False)
        ResumeLayout(False)
    End Sub

    Friend WithEvents PreviewSplit As System.Windows.Forms.SplitContainer
    Friend WithEvents PreviewSidebar As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents RenderTogglesPanel As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents CheckBoxRenderUnderarmor As System.Windows.Forms.CheckBox
    Friend WithEvents CheckBoxRenderArmor As System.Windows.Forms.CheckBox
    Friend WithEvents CheckBoxRenderHeadwear As System.Windows.Forms.CheckBox
    Friend WithEvents CheckBoxRenderGore As System.Windows.Forms.CheckBox
    Friend WithEvents PreviewHostPanel As System.Windows.Forms.Panel
    Friend WithEvents RootLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents GroupBoxWeight As System.Windows.Forms.GroupBox
    Friend WithEvents WeightLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents WeightTriangle As WeightTriangleControl
    Friend WithEvents WeightLegend As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents LabelMuscular As System.Windows.Forms.Label
    Friend WithEvents SliderMuscular As FO4_Base_Library.TinySliderTextBox
    Friend WithEvents LabelThin As System.Windows.Forms.Label
    Friend WithEvents SliderThin As FO4_Base_Library.TinySliderTextBox
    Friend WithEvents LabelFat As System.Windows.Forms.Label
    Friend WithEvents SliderFat As FO4_Base_Library.TinySliderTextBox
    Friend WithEvents GroupBoxMrsv As System.Windows.Forms.GroupBox
    Friend WithEvents MrsvLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents GroupBoxBodySlide As System.Windows.Forms.GroupBox
    Friend WithEvents BodySlideLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents TextBoxBodySlideFilter As System.Windows.Forms.TextBox
    Friend WithEvents BodySlidePanel As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents ButtonOk As System.Windows.Forms.Button
    Friend WithEvents ButtonCancel As System.Windows.Forms.Button
    Friend WithEvents ButtonResetSection As System.Windows.Forms.Button
    Friend WithEvents BottomLayout As System.Windows.Forms.FlowLayoutPanel
End Class
