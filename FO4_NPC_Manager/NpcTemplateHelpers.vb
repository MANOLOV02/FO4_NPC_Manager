Imports System.Globalization
Imports System.IO
Imports System.Drawing
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports FO4_Base_Library
Imports MaterialLib
Imports NiflySharp
Imports NiflySharp.Blocks
Imports OpenTK.Mathematics

''' <summary>Pure stateless NPC template-flag helpers extracted from MainForm (no instance state,
''' no UI). Real separate class (NOT a partial). See project_mainform_split.</summary>
Friend NotInheritable Class NpcTemplateHelpers
    Private Sub New()
    End Sub

    Public Shared Function HasTemplateFlag(flags As UShort, category As NPC_TemplateCategory) As Boolean
        Dim mask = CUShort(1 << CInt(category))
        Return (flags And mask) <> 0US
    End Function

    Public Shared Function ResolveTemplateSourceFormID(npc As NPC_Data, category As NPC_TemplateCategory) As UInteger
        Dim specificFormID As UInteger = 0UI
        If npc.TemplateActorFormIDs.TryGetValue(category, specificFormID) AndAlso specificFormID <> 0UI Then
            Return specificFormID
        End If

        Return npc.TemplateFormID
    End Function

    ''' <summary>True if this NPC inherits its visual appearance (Traits or ModelAnimation) from any template.
    ''' Such NPCs are generic — their look is defined by the template chain, not by themselves.</summary>
    Public Shared Function NpcInheritsVisualAppearance(npc As NPC_Data) As Boolean
        If npc Is Nothing OrElse npc.TemplateFlags = 0US Then Return False
        Return HasTemplateFlag(npc.TemplateFlags, NPC_TemplateCategory.Traits) OrElse
               HasTemplateFlag(npc.TemplateFlags, NPC_TemplateCategory.ModelAnimation)
    End Function

End Class
