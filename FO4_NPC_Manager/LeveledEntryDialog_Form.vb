Imports System.Windows.Forms

''' <summary>Small modal prompting the per-entry data when adding an item into a leveled list via the Edit
''' Outfit "Add to lvl" button: Level / Count / Chance None (the LVLO fields). The reference (the item) is
''' decided by the caller; this only collects the entry parameters.</summary>
Public Class LeveledEntryDialog_Form

    Public Sub New(itemDescription As String)
        InitializeComponent()
        LabelItem.Text = itemDescription
    End Sub

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
