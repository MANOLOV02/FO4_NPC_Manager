Imports System.IO
Imports System.Text.Json

''' <summary>
''' NPC_Manager-specific configuration. Persists to its own npc_config.json next to the
''' executable, separate from the shared library config.json (which carries FO4ExePath +
''' Setting_Lightrig + skinning/render options that WM also consumes). Settings only ever
''' read by NPC_Manager belong here, never in <see cref="FO4_Base_Library.Config_App"/>.
''' </summary>
Public Class NPC_Config

    Public Shared Property Current As New NPC_Config()

    ''' <summary>"Render gore" toggle in the preview toolbar. Default = True to match the
    ''' designer-time CheckBoxRenderGore.Checked, so first-run users see the same UI state
    ''' the form has always shown.</summary>
    Public Property RenderGore As Boolean = True

    ''' <summary>BA2 header version written when packing the baked CharGen for FO4 (per-app setting).
    ''' See Ba2VersionUI for the values. Passed to NpcFaceGenPacker.PackBatch → PackagerRequest.Ba2Version
    ''' (skipped when 0 = Loose-only sentinel).</summary>
    Public Property Ba2Version_FO4 As UInteger = Ba2VersionUI.NextGen

    ''' <summary>"Remove 'Is CharGen Face Preset' flag" option in the Save ESP dialog (sub-option of the
    ''' CharGen bake). When True (default) the saved NPC overrides get ACBS bit 0x04 cleared so the engine
    ''' loads the baked FaceGen instead of reconstructing the face at runtime. Persisted per-app: the dialog
    ''' seeds the checkbox from this and writes it back on toggle (flushed to npc_config.json on app close,
    ''' same as Ba2Version_FO4).</summary>
    Public Property RemoveCharGenFlagOnBake As Boolean = True

    ''' <summary>"Apply fix to ghoul headrear" toggle (CharGen Options UI). When True, the ghoul-female
    ''' head-rear nape is given the vanilla-UV body texture via a disk clone (see MainForm IsGhoulHeadRearCase /
    ''' ApplyGhoulHeadRearClonedTextures). Default False = opt-in; the fix does nothing unless enabled.
    ''' Persisted to npc_config.json (flushed on app close, same as RenderGore).</summary>
    Public Property ApplyGhoulHeadRearFix As Boolean = False

    ''' <summary>"Show:" tree category-filter checkboxes (Section 1 of the NPC tree). Persisted per-app so
    ''' the filter selection survives restarts. Defaults match the WinForms Designer defaults: Unique faces
    ''' on, the rest off. Seeded into the checkboxes on MainForm load; written back in MainForm_FormClosing
    ''' (flushed to npc_config.json by the same SaveConfig() that persists RenderGore).</summary>
    Public Property ShowCatUnique As Boolean = True
    Public Property ShowCatGeneric As Boolean = False
    Public Property ShowCatTemplate As Boolean = False
    Public Property ShowCatUnused As Boolean = False

    Private Shared ReadOnly ConfigFilePath As String = Path.Combine(Application.StartupPath, "npc_config.json")

    Public Shared Sub SaveConfig()
        JsonConfigIO.Save(Current, ConfigFilePath, "NPC_Manager configuration")
    End Sub

    Public Shared Sub LoadConfig()
        Dim cfg = JsonConfigIO.Load(Of NPC_Config)(ConfigFilePath, "NPC_Manager configuration")
        If cfg IsNot Nothing Then Current = cfg
    End Sub
End Class
