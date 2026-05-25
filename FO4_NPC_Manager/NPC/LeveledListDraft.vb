''' <summary>A leveled item list (LVLI) being authored in the Edit Outfit editor — an in-memory draft
''' that lives in MainForm (process scope) until persisted via the Save dialog, at which point the writer
''' emits it as a real LVLI in the output plugin. Mirrors <see cref="OutfitDraft"/> but for the LVLI record
''' type so an outfit can reference a user-built leveled slot (and LVLIs can nest other LVLIs).
'''
''' Record layout it maps to (wbDefinitionsFO4.pas LVLI):
'''   LVLD Chance None (u8) · LVLM Max Count (u8) · LVLF Flags (u8) · LLCT Count (u8) · N×LVLO entries.
''' Each LVLO entry = Level(u16) + pad(u16) + Reference(u32) + Count(u16) + ChanceNone(u8) + pad(u8) = 12B
''' (verified against RecordParsers.ParseLeveledEntry). The Reference is an ARMO or another LVLI.
'''
''' New drafts get a PROVISIONAL FormID (high byte 0xFF, shared sentinel scheme with <see cref="OutfitDraft"/>;
''' allocated from MainForm's single draft-FormID counter so OTFT and LVLI drafts never collide). The writer
''' rewrites it to (selfMasterIndex &lt;&lt; 24 | objectIndex) on save and remaps every reference to it.</summary>
Public Class LeveledListDraft

    ''' <summary>EditorID prefix so author-built leveled lists are identifiable / namespaced in xEdit.</summary>
    Public Const EditorIdPrefix As String = "npcm_LVLI_"

    Public Property FormID As UInteger
    Public Property EditorID As String = ""

    ''' <summary>LVLD — whole-list chance the list yields nothing (0-100). Default 0.</summary>
    Public Property ChanceNone As Byte = 0
    ''' <summary>LVLM — Max Count (0 = unlimited). Default 0.</summary>
    Public Property MaxCount As Byte = 0

    ''' <summary>LVLF 0x01 — Calculate from all levels &lt;= player's level.</summary>
    Public Property CalcAllLevels As Boolean = False
    ''' <summary>LVLF 0x02 — Calculate for each item in count.</summary>
    Public Property CalcEachInCount As Boolean = False
    ''' <summary>LVLF 0x04 — Use All (include every entry instead of rolling one).</summary>
    Public Property UseAll As Boolean = False

    ''' <summary>LVLO entries — each references an ARMO or another LVLI, with per-entry level/count/chance.</summary>
    Public ReadOnly Property Entries As New List(Of LeveledEntry)

    Public Property IsNew As Boolean = True
    Public Property IsModified As Boolean = False

    ''' <summary>Either flag set → "Save new outfits" must (re)write it. Both cleared after save.</summary>
    Public ReadOnly Property IsDirty As Boolean
        Get
            Return IsNew OrElse IsModified
        End Get
    End Property

    ''' <summary>Pack the three LVLF flag bits into the on-disk byte.</summary>
    Public Function FlagsByte() As Byte
        Dim b As Integer = 0
        If CalcAllLevels Then b = b Or &H1
        If CalcEachInCount Then b = b Or &H2
        If UseAll Then b = b Or &H4
        Return CByte(b)
    End Function

    Public Function Clone() As LeveledListDraft
        Dim c As New LeveledListDraft With {
            .FormID = FormID, .EditorID = EditorID, .ChanceNone = ChanceNone, .MaxCount = MaxCount,
            .CalcAllLevels = CalcAllLevels, .CalcEachInCount = CalcEachInCount, .UseAll = UseAll,
            .IsNew = IsNew, .IsModified = IsModified
        }
        For Each e In Entries
            c.Entries.Add(New LeveledEntry With {.RefFormID = e.RefFormID, .Level = e.Level, .Count = e.Count, .ChanceNone = e.ChanceNone})
        Next
        Return c
    End Function

    ''' <summary>One LVLO entry: a reference (ARMO or LVLI) with its level / count / chance-none.</summary>
    Public Class LeveledEntry
        Public RefFormID As UInteger
        Public Level As UShort = 1
        Public Count As UShort = 1
        Public ChanceNone As Byte = 0
    End Class
End Class
