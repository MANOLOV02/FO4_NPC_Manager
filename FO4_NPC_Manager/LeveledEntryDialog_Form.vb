Imports System.Windows.Forms

''' <summary>Small modal prompting the per-entry data when adding an item into a leveled list via the Edit
''' Outfit "Add to lvl" button: Level / Count / Chance None (the LVLO fields). The reference (the item) is
''' decided by the caller; this only collects the entry parameters.</summary>
Public Class LeveledEntryDialog_Form

    ''' <summary>Optional <paramref name="initialLevel"/>/<paramref name="initialCount"/>/
    ''' <paramref name="initialChance"/> seed the numerics so the same dialog EDITS an existing LVLO entry (the
    ''' recursive leveled-list drill-down "Edit entry") instead of only adding a fresh one. Values are clamped into
    ''' each numeric's configured Min/Max so a source value outside the editor's range can't throw.</summary>
    Public Sub New(itemDescription As String,
                   Optional initialLevel As UShort = 1,
                   Optional initialCount As UShort = 1,
                   Optional initialChance As Byte = 0)
        InitializeComponent()
        LabelItem.Text = itemDescription
        NumericLevel.Value = ClampToRange(NumericLevel, initialLevel)
        NumericCount.Value = ClampToRange(NumericCount, initialCount)
        NumericChanceNone.Value = ClampToRange(NumericChanceNone, initialChance)
    End Sub

    ''' <summary>Clamp <paramref name="v"/> into a numeric's [Minimum, Maximum] so seeding never throws.</summary>
    Private Shared Function ClampToRange(n As NumericUpDown, v As Integer) As Decimal
        Dim d As Decimal = v
        If d < n.Minimum Then Return n.Minimum
        If d > n.Maximum Then Return n.Maximum
        Return d
    End Function

    Public ReadOnly Property LevelValue As UShort
        Get
            Return CUShort(NumericLevel.Value)
        End Get
    End Property

    Public ReadOnly Property CountValue As UShort
        Get
            Return CUShort(NumericCount.Value)
        End Get
    End Property

    Public ReadOnly Property ChanceNoneValue As Byte
        Get
            Return CByte(NumericChanceNone.Value)
        End Get
    End Property
End Class
