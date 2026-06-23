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

''' <summary>Build per-bucket NPC state (traits/inventory/model) + resolve body weights. Extracted from MainForm (pure stateless, no instance state, no UI). Real separate
''' class (NOT a partial). See project_mainform_split.</summary>
Friend NotInheritable Class NpcStateFactory
    Private Sub New()
    End Sub

    ''' <summary>Materialize NPC.MWGT into 3 concrete floats, applying the engine's "Default"
    ''' sentinel substitution rule. Each NPC.MWGT slot may come as Nothing (the parser flagged
    ''' it as Single.MaxValue, the wire encoding of "field not assigned" — see
    ''' RecordParsers.ReadOptionalFloat). Substitution rule:
    '''   • 0 Defaults → return as-is, do NOT renormalize (respect the record's data even if
    '''     it doesn't sum to 1).
    '''   • 1 Default  → fill the missing slot with clamp(1 - sum(other 2), 0, +∞). The two
    '''     explicit values stay untouched. Result sums to 1 unless the two explicit values
    '''     exceeded 1 (in which case the missing slot is 0 and the sum stays > 1).
    '''   • 2 Defaults → fill the missing slots from RACE.{Male|Female}DefaultWeight{X}, then
    '''     renormalize the 3 to sum=1 (skip if total is 0).
    '''   • 3 Defaults → use RACE.{Male|Female}DefaultWeight{X} verbatim; do NOT renormalize.
    ''' RACE defaults are read per-gender. If RACE doesn't carry the field (record &lt; v109),
    ''' fallback is 0.
    ''' Logs the raw → resolved transition when any substitution happened, for audit.</summary>
    Public Shared Function ResolveBodyWeights(traits As MainForm.TraitsState, race As RACE_Data, isFemale As Boolean) As (Thin As Single, Muscular As Single, Fat As Single)
        Dim rawT = traits.WeightThin
        Dim rawM = traits.WeightMuscular
        Dim rawF = traits.WeightFat
        Dim defaultCount = 0
        If Not rawT.HasValue Then defaultCount += 1
        If Not rawM.HasValue Then defaultCount += 1
        If Not rawF.HasValue Then defaultCount += 1

        Dim resT As Single, resM As Single, resF As Single

        Select Case defaultCount
            Case 0
                resT = rawT.Value
                resM = rawM.Value
                resF = rawF.Value
            Case 1
                Dim a As Single, b As Single
                If Not rawT.HasValue Then
                    a = rawM.Value : b = rawF.Value
                    resT = Math.Max(0.0F, 1.0F - a - b) : resM = a : resF = b
                ElseIf Not rawM.HasValue Then
                    a = rawT.Value : b = rawF.Value
                    resT = a : resM = Math.Max(0.0F, 1.0F - a - b) : resF = b
                Else
                    a = rawT.Value : b = rawM.Value
                    resT = a : resM = b : resF = Math.Max(0.0F, 1.0F - a - b)
                End If
            Case 2
                Dim raceT = If(isFemale, race.FemaleDefaultWeightThin, race.MaleDefaultWeightThin).GetValueOrDefault(0.0F)
                Dim raceM = If(isFemale, race.FemaleDefaultWeightMuscular, race.MaleDefaultWeightMuscular).GetValueOrDefault(0.0F)
                Dim raceF = If(isFemale, race.FemaleDefaultWeightFat, race.MaleDefaultWeightFat).GetValueOrDefault(0.0F)
                resT = If(rawT, raceT)
                resM = If(rawM, raceM)
                resF = If(rawF, raceF)
                Dim sum = resT + resM + resF
                If sum > 0.0F Then
                    resT /= sum : resM /= sum : resF /= sum
                End If
            Case Else  ' 3
                resT = If(isFemale, race.FemaleDefaultWeightThin, race.MaleDefaultWeightThin).GetValueOrDefault(0.0F)
                resM = If(isFemale, race.FemaleDefaultWeightMuscular, race.MaleDefaultWeightMuscular).GetValueOrDefault(0.0F)
                resF = If(isFemale, race.FemaleDefaultWeightFat, race.MaleDefaultWeightFat).GetValueOrDefault(0.0F)
        End Select

        If defaultCount > 0 Then
            Dim rawStr = $"({(If(rawT.HasValue, rawT.Value.ToString("F3"), "Default"))},{(If(rawM.HasValue, rawM.Value.ToString("F3"), "Default"))},{(If(rawF.HasValue, rawF.Value.ToString("F3"), "Default"))})"
        End If

        Return (resT, resM, resF)
    End Function

    Public Shared Function CreateOwnTraitsState(npc As NPC_Data) As MainForm.TraitsState
        ' [TEST: TPLT-traits-bucket] HeadTexture/HairColor/FacialHairColor/HeadParts/QNAM
        ' now seeded here so they ride the Traits chain walk.
        Dim state As New MainForm.TraitsState With {
            .SourceFormID = npc.FormID,
            .IsFemale = npc.IsFemale,
            .RaceFormID = npc.RaceFormID,
            .SkinFormID = npc.SkinFormID,
            .WeightThin = npc.WeightThin,
            .WeightMuscular = npc.WeightMuscular,
            .WeightFat = npc.WeightFat,
            .HeadTextureFormID = npc.HeadTextureFormID,
            .HairColorFormID = npc.HairColorFormID,
            .FacialHairColorFormID = npc.FacialHairColorFormID,
            .HasTextureLighting = npc.HasTextureLighting,
            .TextureLightingColor = npc.TextureLightingColor
        }
        state.HeadPartFormIDs.AddRange(npc.HeadPartFormIDs)
        Return state
    End Function

    Public Shared Function CreateOwnInventoryState(npc As NPC_Data) As MainForm.InventoryState
        Return New MainForm.InventoryState With {
            .DefaultOutfitFormID = npc.DefaultOutfitFormID,
            .SleepOutfitFormID = npc.SleepOutfitFormID
        }
    End Function

    Public Shared Function CreateOwnModelAnimationState(npc As NPC_Data) As MainForm.ModelAnimationState
        ' [TEST: TPLT-traits-bucket] Face-appearance fields moved to CreateOwnTraitsState.
        Dim state As New MainForm.ModelAnimationState
        state.ObjectTemplateOMODFormIDs.AddRange(npc.ObjectTemplateOMODFormIDs)
        state.ObjectTemplateCombinations.AddRange(npc.ObjectTemplateCombinations)
        state.HasObjectTemplate = npc.HasObjectTemplate
        If npc.AttachParentSlotFormIDs IsNot Nothing Then
            state.AttachParentSlotFormIDs.AddRange(npc.AttachParentSlotFormIDs)
        End If
        Return state
    End Function


    ''' <summary>The FormID to read FACE/BODY appearance (tint, chargen + face-bone morphs, MRSV,
    ''' skin-tone, FaceGen NIF) from: the resolved Traits source (inherited) when set, else the NPC's
    ''' own FormID. For a non-inheriting NPC this equals the root, so every read is byte-identical to
    ''' before — only template-inheriting NPCs change. Mirrors how HeadPartFormIDs/Hair already resolve
    ''' from the Traits source. Replaces the old ModelSourceFormID-or-root pattern, which always fell to
    ''' root because ModelSourceFormID was never wired in the render path.</summary>
    Public Shared Function FaceAppearanceSourceFormID(state As MainForm.NPCVisualState) As UInteger
        If state Is Nothing Then Return 0UI
        Return If(state.TraitsSourceFormID <> 0UI, state.TraitsSourceFormID, state.FormID)
    End Function
End Class
