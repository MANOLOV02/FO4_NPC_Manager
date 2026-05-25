Imports FO4_Base_Library

''' <summary>
''' Per-shape material override helpers shared by the human path (CollectArmoCandidates →
''' ApplyShapeMaterialOverrides) and the upcoming NPC ObjectTemplate / OMOD path. Lives in
''' NPC_Manager because no other app currently consumes it; promote to FO4_Base_Library only
''' when a second app needs it (feedback_always_correct_path_no_optional_debt.md).
'''
''' Two operations, each with a FunctionType enum matching the engine binary at
''' wbDefinitionsFO4.pas:5839-5842. The ARMA-direct path always passes SET (single
''' unconditional swap by gender). The OBTS/OMOD path will pass the FunctionType decoded
''' from the OMOD Property (idx 13 MaterialSwaps for FormID, idx 12 ColorRemappingIndex
''' for Float, per wbArmorPropertyEnum at :5702-5717).
'''
'''   • ApplyMaterialSwap (FormID-typed, ops SET / REM / ADD per :5842):
'''       SET — load MSWP, replace materials whose path matches OriginalMaterial. Subsequent
'''             SETs operate on the already-mutated material; semantically identical to ADD
'''             once a previous swap landed (the engine accumulates by mutating in place).
'''       ADD — same code path as SET. The distinction is conceptual (modder intent: "stack
'''             on top of whatever's there") but produces the same render output because the
'''             aplicador opera sobre el material actual del shape, sea original o post-swap.
'''       REM — would need to revert a previously applied swap. No vanilla case observed; we
'''             trip Debugger.Break and log so we capture the first occurrence with the OMOD
'''             FormID for analysis before designing the revert algorithm.
'''
'''   • ApplyColorRemap (Float-typed, ops SET / MUL+ADD / ADD per :5839):
'''       SET     — overwrite material.GrayscaleToPaletteScale = value1.
'''       ADD     — material.GrayscaleToPaletteScale += value1.
'''       MUL+ADD — material.GrayscaleToPaletteScale = current * value1 + value2. No vanilla
'''                 case observed; same Debugger.Break pattern as REM.
'''
''' Engine palette LUT addressing: the shader samples a column of the BGSM's GreyscaleTexture
''' according to GrayscaleToPaletteScale (0..1 → texel 0..width). We do NOT force
''' GrayscaleToPaletteColor=True; if the BGSM didn't ship the palette opt-in the setter is
''' a visual no-op — engine-faithful (the modder is expected to pair ColorRemappingIndex on
''' ARMA/OMOD with a palette-enabled BGSM).
'''
''' Order in ApplyShapeMaterialOverrides: MSWP first (mutates material), then ColorRemap on
''' the post-swap material. For ARMA-direct both arrive as SET. For OBTS/OMOD the caller
''' walks the combination's Includes in declaration order, calling these helpers per Property.
''' </summary>
Friend Module ShapeMaterialOverrides

    ''' <summary>FormID-typed Property function (idx 4 / 6 ValueType per :5833-5835), used by
    ''' MSWP property idx 13. Binary values per wbDefinitionsFO4.pas:5842.</summary>
    Friend Enum MaterialSwapFunction As Byte
        [SET] = 0
        Remov = 1
        ADD = 2
    End Enum

    ''' <summary>Float-typed Property function (idx 1 ValueType), used by ColorRemappingIndex
    ''' property idx 12. Binary values per wbDefinitionsFO4.pas:5839.</summary>
    Friend Enum ColorRemapFunction As Byte
        [SET] = 0
        MUL_ADD = 1
        ADD = 2
    End Enum

    Friend Sub ApplyMaterialSwap(mswpFormID As UInteger,
                                 funcType As MaterialSwapFunction,
                                 shapes As IEnumerable(Of IRenderableShape),
                                 pluginManager As PluginManager)
        Logger.LogLazy(Function() $"[MSWP-ENTRY] mswp=0x{mswpFormID:X8} func={funcType}")

        If mswpFormID = 0UI Then Return
        If pluginManager Is Nothing Then Return

        Dim mswpRec = pluginManager.GetRecord(mswpFormID)
        If mswpRec Is Nothing OrElse mswpRec.Header.Signature <> "MSWP" Then
            Logger.LogLazy(Function() $"[MSWP-LOAD-FAIL] mswp=0x{mswpFormID:X8} reason='record-not-found-or-wrong-sig'")
            Return
        End If

        Dim mswp = RecordParsers.ParseMSWP(mswpRec, pluginManager)
        If mswp.Substitutions.Count = 0 Then
            Logger.LogLazy(Function() $"[MSWP-LOAD] mswp=0x{mswpFormID:X8} subs=0 (empty MSWP)")
            Return
        End If

        Dim subsCount = mswp.Substitutions.Count
        Logger.LogLazy(Function() $"[MSWP-LOAD] mswp=0x{mswpFormID:X8} subs={subsCount}")

        ' SET / ADD / REM share ONE direction-aware path. SET and ADD both match a shape's current material
        ' against a substitution's OriginalMaterial and swap in the ReplacementMaterial — conceptually SET =
        ' "first swap", ADD = "stack on a previous swap", but identical in-place mutation (the engine
        ' accumulates by walking the OMODs in order, each call mutating whatever the previous left). REM is
        ' the EXACT inverse: it matches the ReplacementMaterial and reverts to the OriginalMaterial, undoing
        ' a prior SET/ADD of this same MSWP. Paint OMODs (e.g. DLC05 Abraxo) REM the base material swap
        ' before ADDing their own, so the new swap — which maps from the original NIF material — matches.
        Dim isRemove = (funcType = MaterialSwapFunction.Remov)
        Dim dirLog = If(isRemove, "REM", "SET/ADD")
        For Each shape In shapes
            MainForm.EnsureShapeMaterialResolved(shape)

            Dim relatedMaterial = shape.ShapeMaterial
            Dim shapeNameLog = shape.ShapeName
            If relatedMaterial Is Nothing OrElse relatedMaterial.material Is Nothing Then
                Logger.LogLazy(Function() $"[MSWP-SHAPE-SKIP] shape='{shapeNameLog}' reason='no-material'")
                Continue For
            End If

            Dim currentPath = If(relatedMaterial.path, "").Trim()
            If currentPath = "" Then
                Logger.LogLazy(Function() $"[MSWP-SHAPE-SKIP] shape='{shapeNameLog}' reason='empty-current-path'")
                Continue For
            End If

            Dim correctedCurrentPath = FO4UnifiedMaterial_Class.CorrectMaterialPath(currentPath)
            Dim ccpLog = correctedCurrentPath
            Logger.LogLazy(Function() $"[MSWP-SHAPE] shape='{shapeNameLog}' currentPath='{ccpLog}' dir={dirLog}")

            Dim matched As Boolean = False
            For Each sub_ In mswp.Substitutions
                ' Direction: SET/ADD match Original→Replacement; REM matches Replacement→Original.
                Dim fromPath = FO4UnifiedMaterial_Class.CorrectMaterialPath(If(If(isRemove, sub_.ReplacementMaterial, sub_.OriginalMaterial), ""))
                If fromPath = "" Then Continue For

                If String.Equals(correctedCurrentPath, fromPath, StringComparison.OrdinalIgnoreCase) Then
                    Dim targetPath = If(If(isRemove, sub_.OriginalMaterial, sub_.ReplacementMaterial), "")
                    Dim toL = targetPath
                    Dim fromL = fromPath
                    If targetPath = "" Then
                        Logger.LogLazy(Function() $"[MSWP-MATCH-EMPTY-TARGET] shape='{shapeNameLog}' from='{fromL}' target='' dir={dirLog}")
                        matched = True
                        Exit For
                    End If

                    Dim newMaterial = MainForm.TryLoadMaterialFromDictionary(targetPath, relatedMaterial.material, shape.NifShape, shape.NifContent)
                    If newMaterial IsNot Nothing Then
                        relatedMaterial.material = newMaterial
                        relatedMaterial.path = FO4UnifiedMaterial_Class.CorrectMaterialPath(targetPath)
                        Logger.LogLazy(Function() $"[MSWP-APPLIED] shape='{shapeNameLog}' from='{fromL}' → to='{toL}' dir={dirLog} loadResult=OK")
                    Else
                        Logger.LogLazy(Function() $"[MSWP-APPLIED-LOAD-FAIL] shape='{shapeNameLog}' from='{fromL}' → to='{toL}' dir={dirLog} loadResult=NULL — material unchanged")
                    End If
                    matched = True
                    Exit For
                End If
            Next
            If Not matched Then
                Logger.LogLazy(Function() $"[MSWP-NO-MATCH] shape='{shapeNameLog}' currentPath='{ccpLog}' subs={subsCount} dir={dirLog} — no substitution matched")
            End If
        Next
    End Sub

    Friend Sub ApplyColorRemap(value1 As Single,
                               value2 As Single,
                               funcType As ColorRemapFunction,
                               shapes As IEnumerable(Of IRenderableShape))
        Logger.LogLazy(Function() $"[CREMAP-ENTRY] func={funcType} v1={value1:F4} v2={value2:F4}")

        If shapes Is Nothing Then Return

        If funcType = ColorRemapFunction.MUL_ADD Then
            ' Placeholder mirroring the REM stub above. Engine formula per :5839 is
            ' `current * value1 + value2`, but no vanilla case observed yet; trip the
            ' debugger on first occurrence so we can validate the formula against in-game
            ' rendering before committing to it.
            Logger.LogLazy(Function() $"[CREMAP-MUL_ADD-STUB] v1={value1:F4} v2={value2:F4} — MUL_ADD not implemented, no-op")
            Debugger.Break()
            Return
        End If

        For Each shape In shapes
            MainForm.EnsureShapeMaterialResolved(shape)

            Dim relatedMaterial = shape.ShapeMaterial
            Dim shapeNameLog = shape.ShapeName
            If relatedMaterial Is Nothing Then
                Logger.LogLazy(Function() $"[CREMAP-SHAPE-SKIP] shape='{shapeNameLog}' reason='no-related-material'")
                Continue For
            End If

            Dim material = relatedMaterial.material
            If material Is Nothing Then
                Logger.LogLazy(Function() $"[CREMAP-SHAPE-SKIP] shape='{shapeNameLog}' reason='material-Nothing'")
                Continue For
            End If

            Dim oldScale = material.GrayscaleToPaletteScale
            Dim paletteEnabled = material.GrayscaleToPaletteColor
            Dim paletteTex = If(material.GreyscaleTexture, "")
            Dim newScale As Single
            Select Case funcType
                Case ColorRemapFunction.SET
                    newScale = value1
                Case ColorRemapFunction.ADD
                    newScale = oldScale + value1
                Case Else
                    Continue For ' unreachable (MUL_ADD handled above)
            End Select

            material.GrayscaleToPaletteScale = newScale

            Dim oldL = oldScale, newL = newScale, palL = paletteEnabled, palTexL = paletteTex
            Logger.LogLazy(Function() $"[CREMAP-APPLIED] shape='{shapeNameLog}' palEnabled={palL} palTex='{palTexL}' oldScale={oldL:F4} → newScale={newL:F4}")
            If Not paletteEnabled Then
                Logger.LogLazy(Function() $"[CREMAP-NO-PALETTE] shape='{shapeNameLog}' newScale={newL:F4} but GrayscaleToPaletteColor=False — visual no-op (engine-faithful: caller responsible for palette-enabled BGSM)")
            End If
        Next
    End Sub

End Module
