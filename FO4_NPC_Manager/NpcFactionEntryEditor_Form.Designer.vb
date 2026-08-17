' UI built in Designer per 00-reglas-ui-y-vb.md. InitializeComponent is declarative ONLY.
' Modal editor for a SINGLE NPC_FactionEntry (FACT FormID + s8 Rank) of an NPC's SNAM faction list —
' mirror of ArmoDamageResistEditor_Form so the "Factions" grid stays pure read-only.
<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class NpcFactionEntryEditor_Form
    ' Hereda de FO4_Base_Library.IconFormBase, que aporta los ImageList compartidos IconsSmall (16x16)
    ' e IconsLarge (24x24): los iconos viven UNA sola vez, en el resx de ese formulario base.
    ' El formulario base NO tiene controles y no fija Size/Text/Icon/AutoScale, asi que heredar de
    ' el no cambia el aspecto de nada. Ver el remarks de IconFormBase.vb.
    ' ⛔ Los iconos se eligen SIEMPRE por ImageKey, nunca por ImageIndex: el orden del ImageList
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
        LabelFaction = New Label()
        TextBoxFaction = New TextBox()
        ButtonPickFaction = New Button()
        LabelRank = New Label()
        NumRank = New NumericUpDown()
        BottomLayout = New FlowLayoutPanel()
        ButtonOk = New Button()
        ButtonCancel = New Button()
        RootLayout.SuspendLayout()
        CType(NumRank, ComponentModel.ISupportInitialize).BeginInit()
        BottomLayout.SuspendLayout()
        SuspendLayout()
        '
        ' RootLayout
        '
        RootLayout.ColumnCount = 3
        RootLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 140F))
        RootLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        RootLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 40F))
        RootLayout.Controls.Add(LabelFaction, 0, 0)
        RootLayout.Controls.Add(TextBoxFaction, 1, 0)
        RootLayout.Controls.Add(ButtonPickFaction, 2, 0)
        RootLayout.Controls.Add(LabelRank, 0, 1)
        RootLayout.Controls.Add(NumRank, 1, 1)
        RootLayout.Controls.Add(BottomLayout, 1, 2)
        RootLayout.SetColumnSpan(BottomLayout, 2)
        RootLayout.Dock = DockStyle.Fill
        RootLayout.Location = New Point(0, 0)
        RootLayout.Name = "RootLayout"
        RootLayout.Padding = New Padding(10)
        RootLayout.RowCount = 3
        RootLayout.RowStyles.Add(New RowStyle())
        RootLayout.RowStyles.Add(New RowStyle())
        RootLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        RootLayout.Size = New Size(520, 130)
        RootLayout.TabIndex = 0
        '
        ' LabelFaction
        '
        LabelFaction.Anchor = AnchorStyles.Left
        LabelFaction.AutoSize = True
        LabelFaction.Location = New Point(13, 18)
        LabelFaction.Name = "LabelFaction"
        LabelFaction.Size = New Size(110, 15)
        LabelFaction.TabIndex = 0
        LabelFaction.Text = "Faction (SNAM):"
        '
        ' TextBoxFaction
        '
        TextBoxFaction.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        TextBoxFaction.Location = New Point(153, 14)
        TextBoxFaction.Name = "TextBoxFaction"
        TextBoxFaction.ReadOnly = True
        TextBoxFaction.Size = New Size(314, 23)
        TextBoxFaction.TabIndex = 1
        '
        ' ButtonPickFaction
        '
        ButtonPickFaction.Anchor = AnchorStyles.Left
        ButtonPickFaction.Location = New Point(473, 13)
        ButtonPickFaction.Name = "ButtonPickFaction"
        ButtonPickFaction.Size = New Size(34, 24)
        ButtonPickFaction.TabIndex = 2
        ButtonPickFaction.Text = "…"
        ButtonPickFaction.UseVisualStyleBackColor = True
        '
        ' LabelRank
        '
        LabelRank.Anchor = AnchorStyles.Left
        LabelRank.AutoSize = True
        LabelRank.Location = New Point(13, 49)
        LabelRank.Name = "LabelRank"
        LabelRank.Size = New Size(40, 15)
        LabelRank.TabIndex = 3
        LabelRank.Text = "Rank:"
        '
        ' NumRank
        '
        NumRank.Anchor = AnchorStyles.Left
        NumRank.Location = New Point(153, 45)
        NumRank.Maximum = New Decimal(127)
        NumRank.Minimum = New Decimal(New Integer() {128, 0, 0, -2147483648})
        NumRank.Name = "NumRank"
        NumRank.Size = New Size(130, 23)
        NumRank.TabIndex = 4
        NumRank.TextAlign = System.Windows.Forms.HorizontalAlignment.Right
        '
        ' BottomLayout
        '
        BottomLayout.AutoSize = True
        BottomLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink
        BottomLayout.Controls.Add(ButtonCancel)
        BottomLayout.Controls.Add(ButtonOk)
        BottomLayout.Dock = DockStyle.Bottom
        BottomLayout.FlowDirection = FlowDirection.RightToLeft
        BottomLayout.Location = New Point(153, 102)
        BottomLayout.Margin = New Padding(0)
        BottomLayout.Name = "BottomLayout"
        BottomLayout.Size = New Size(354, 25)
        BottomLayout.TabIndex = 5
        '
        ' ButtonOk
        '
        ButtonOk.Location = New Point(271, 0)
        ButtonOk.Margin = New Padding(3, 0, 3, 0)
        ButtonOk.Name = "ButtonOk"
        ButtonOk.Size = New Size(80, 25)
        ButtonOk.TabIndex = 0
        ButtonOk.Text = "OK"
        ButtonOk.UseVisualStyleBackColor = True
        '
        ' ButtonCancel
        '
        ButtonCancel.DialogResult = DialogResult.Cancel
        ButtonCancel.Location = New Point(185, 0)
        ButtonCancel.Margin = New Padding(3, 0, 3, 0)
        ButtonCancel.Name = "ButtonCancel"
        ButtonCancel.Size = New Size(80, 25)
        ButtonCancel.TabIndex = 1
        ButtonCancel.Text = "Cancel"
        ButtonCancel.UseVisualStyleBackColor = True
        '
        ' NpcFactionEntryEditor_Form
        '
        AcceptButton = ButtonOk
        AutoScaleDimensions = New SizeF(7F, 15F)
        AutoScaleMode = AutoScaleMode.Font
        CancelButton = ButtonCancel
        ClientSize = New Size(520, 130)
        Controls.Add(RootLayout)
        Font = New Font("Segoe UI", 9F)
        MinimizeBox = False
        Name = "NpcFactionEntryEditor_Form"
        StartPosition = FormStartPosition.CenterParent
        Text = "Faction (SNAM)"
        RootLayout.ResumeLayout(False)
        RootLayout.PerformLayout()
        CType(NumRank, ComponentModel.ISupportInitialize).EndInit()
        BottomLayout.ResumeLayout(False)
        ResumeLayout(False)
    End Sub

    Friend WithEvents RootLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents LabelFaction As System.Windows.Forms.Label
    Friend WithEvents TextBoxFaction As System.Windows.Forms.TextBox
    Friend WithEvents ButtonPickFaction As System.Windows.Forms.Button
    Friend WithEvents LabelRank As System.Windows.Forms.Label
    Friend WithEvents NumRank As System.Windows.Forms.NumericUpDown
    Friend WithEvents BottomLayout As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents ButtonOk As System.Windows.Forms.Button
    Friend WithEvents ButtonCancel As System.Windows.Forms.Button
End Class
