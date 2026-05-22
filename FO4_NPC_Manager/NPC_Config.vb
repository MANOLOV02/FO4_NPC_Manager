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

    ''' <summary>BA2 header version written when packing the baked CharGen for FO4. FO4-only
    ''' (SSE packs BSA v105, unaffected). 8 = Next Gen (default; loads only on NG Fallout4.exe
    ''' 1.10.980+). 1 = Old Gen / universal (loads on both OG and NG). Passed straight to the
    ''' BA2 writer via PackForNpc → PackagerRequest.Ba2Version.</summary>
    Public Property Ba2Version_FO4 As UInteger = 8UI

    Private Shared ReadOnly ConfigFilePath As String = Path.Combine(Application.StartupPath, "npc_config.json")
    Private Shared ReadOnly SaveOptions As New JsonSerializerOptions With {.WriteIndented = True}

    Public Shared Sub SaveConfig()
        Try
            Dim jsonString As String = JsonSerializer.Serialize(Current, SaveOptions)
            File.WriteAllText(ConfigFilePath, jsonString)
        Catch ex As Exception
            MessageBox.Show("Error saving NPC_Manager configuration: " & ex.Message,
                            "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    Public Shared Sub LoadConfig()
        Try
            If File.Exists(ConfigFilePath) Then
                Dim jsonString As String = File.ReadAllText(ConfigFilePath)
                Dim cfg = JsonSerializer.Deserialize(Of NPC_Config)(jsonString)
                If cfg IsNot Nothing Then Current = cfg
            End If
        Catch ex As Exception
            MessageBox.Show("Error loading NPC_Manager configuration: " & ex.Message,
                            "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub
End Class
