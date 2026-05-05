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
        RootLayout = New System.Windows.Forms.TableLayoutPanel()
        GroupBoxWeight = New System.Windows.Forms.GroupBox()
        WeightLayout = New System.Windows.Forms.TableLayoutPanel()
        WeightTriangle = New WeightTriangleControl()
        WeightLegend = New System.Windows.Forms.TableLayoutPanel()
        LabelMuscular = New System.Windows.Forms.Label()
        LabelMuscularValue = New System.Windows.Forms.Label()
        LabelThin = New System.Windows.Forms.Label()
        LabelThinValue = New System.Windows.Forms.Label()
        LabelFat = New System.Windows.Forms.Label()
        LabelFatValue = New System.Windows.Forms.Label()

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
        LabelMuscularValue.Text = "0.00"
        LabelMuscularValue.AutoSize = True
        LabelMuscularValue.MinimumSize = New System.Drawing.Size(50, 0)
        LabelMuscularValue.TextAlign = System.Drawing.ContentAlignment.MiddleRight
        '
        LabelThin.Text = "Thin:"
        LabelThin.AutoSize = True
        LabelThin.TextAlign = System.Drawing.ContentAlignment.MiddleLeft
        LabelThinValue.Text = "0.00"
        LabelThinValue.AutoSize = True
        LabelThinValue.MinimumSize = New System.Drawing.Size(50, 0)
        LabelThinValue.TextAlign = System.Drawing.ContentAlignment.MiddleRight
        '
        LabelFat.Text = "Fat:"
        LabelFat.AutoSize = True
        LabelFat.TextAlign = System.Drawing.ContentAlignment.MiddleLeft
        LabelFatValue.Text = "0.00"
        LabelFatValue.AutoSize = True
        LabelFatValue.MinimumSize = New System.Drawing.Size(50, 0)
        LabelFatValue.TextAlign = System.Drawing.ContentAlignment.MiddleRight
        '
        ' Legend layout: Muscular at top (matches triangle apex), then Thin (bottom-left vertex),
        ' then Fat (bottom-right vertex). Mirrors the triangle's spatial arrangement so the
        ' user can read top→bottom and orient themselves.
        WeightLegend.Controls.Add(LabelMuscular, 0, 0)
        WeightLegend.Controls.Add(LabelMuscularValue, 1, 0)
        WeightLegend.Controls.Add(LabelThin, 0, 1)
        WeightLegend.Controls.Add(LabelThinValue, 1, 1)
        WeightLegend.Controls.Add(LabelFat, 0, 2)
        WeightLegend.Controls.Add(LabelFatValue, 1, 2)
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
        ClientSize = New System.Drawing.Size(620, 640)
        StartPosition = System.Windows.Forms.FormStartPosition.CenterParent
        Controls.Add(RootLayout)

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
        ResumeLayout(False)
    End Sub

    Friend WithEvents RootLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents GroupBoxWeight As System.Windows.Forms.GroupBox
    Friend WithEvents WeightLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents WeightTriangle As WeightTriangleControl
    Friend WithEvents WeightLegend As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents LabelMuscular As System.Windows.Forms.Label
    Friend WithEvents LabelMuscularValue As System.Windows.Forms.Label
    Friend WithEvents LabelThin As System.Windows.Forms.Label
    Friend WithEvents LabelThinValue As System.Windows.Forms.Label
    Friend WithEvents LabelFat As System.Windows.Forms.Label
    Friend WithEvents LabelFatValue As System.Windows.Forms.Label
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
