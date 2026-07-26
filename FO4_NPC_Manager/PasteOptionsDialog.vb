
''' <summary>Modal dialog que permite al usuario elegir qué categorías del clipboard preset aplicar al NPC
''' receptor en Paste Look. Los checkboxes viven en <see cref="PresetCategoryPanel"/> — el MISMO control que
''' hostea el browser de presets LooksMenu/RaceMenu — así Paste y Load nunca ofrecen categorías distintas.
''' Las categorías NO tildadas preservan lo que el NPC receptor muestra hoy; el merge lo hace
''' <see cref="PresetCategoryFilter.BuildFiltered"/>.</summary>
Public Class PasteOptionsDialog

    ''' <param name="isSse">True when the current session is a Skyrim (SSE) game — the panel then collapses
    ''' the FO4-only categories (MRSV body regions, FMRS face bone regions, F4SE LM skin template) and
    ''' reveals the SSE-only ones (per-vertex sculpt, RaceMenu body scale).</param>
    ''' <param name="source">The preset being pasted. Drives the per-category amounts shown next to each
    ''' checkbox and greys out whatever the copied look doesn't carry.</param>
    Public Sub New(Optional isSse As Boolean = False,
                   Optional source As LooksmenuLoader.LooksmenuPreset = Nothing)
        InitializeComponent()
        CategoryPanel.ConfigureGame(isSse)
        CategoryPanel.SetPreset(source)
    End Sub

    ''' <summary>Snapshot the current checkbox states for <see cref="PresetCategoryFilter.BuildFiltered"/>.</summary>
    Public Function BuildOptions() As PresetCategoryOptions
        Return CategoryPanel.Options
    End Function
End Class
