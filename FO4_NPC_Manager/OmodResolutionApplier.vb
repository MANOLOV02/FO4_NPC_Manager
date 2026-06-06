Imports FO4_Base_Library

''' <summary>
''' Applies a CombinationResolution (from ObjectTemplateResolver) to a set of shapes by
''' walking visual Properties in declaration order and dispatching to ShapeMaterialOverrides.
'''
''' Order — verified in dump v2 (vanilla 4473 NPCs / 580 ARMOs):
'''   1. DirectProperties of every applied combination (in record declaration order).
'''   2. Then for each IncludedOmod (in walk order), its own Properties (same dispatch).
'''
''' SET vs ADD semantics fall out naturally from this order: each call to
''' ShapeMaterialOverrides.ApplyMaterialSwap or ApplyColorRemap mutates the shape's current
''' material in place, so a later ADD acts on whatever the previous SET left there.
''' No vanilla case mixes SET+ADD inside a single combination's Properties (33 NPC_ cases
''' have multiple MSWPs of the same op kind), but the order-respecting walk handles both
''' cases uniformly.
'''
''' FormType context (passed by caller) tells the applier which PropertyIndex enum interprets
''' the property's idx — wbArmorPropertyEnum (idx 12 / 13) for ARMO, wbActorPropertyEnum
''' (idx 4 / 5) for NPC_. WEAP is parsed but ignored for shape rendering (weapon meshes are
''' a separate path).
'''
''' Non-visual properties (Keywords, ForcedInventory, Weight, Value, Rating, etc.) are
''' silently skipped — they have no rendering side-effect.
'''
''' OMOD chunks (FormType=NPC_ with non-empty ModelPath) are NOT applied here — they need
''' AttachPoint mounting via BSConnectPoint, which is the next-session work for the robot
''' rendering rewrite. They're logged with [OMOD-MESH-DEFERRED] so we can see where the
''' shapes would have gone.
''' </summary>
Public Module OmodResolutionApplier

    Public Sub ApplyResolutionToShapes(res As ObjectTemplateResolver.CombinationResolution,
                                       formTypeContext As String,
                                       shapes As IEnumerable(Of IRenderableShape),
                                       pm As PluginManager)
        If res Is Nothing OrElse shapes Is Nothing OrElse pm Is Nothing Then Return
        If res.AppliedCombinations.Count = 0 AndAlso res.DirectProperties.Count = 0 AndAlso res.IncludedOmods.Count = 0 Then Return

        Dim logEnabled = Logger.Enabled
        If logEnabled Then
            Dim shapeCount = shapes.Count()
            Dim directCount = res.DirectProperties.Count
            Dim omodCount = res.IncludedOmods.Count
            Logger.LogLazy(Function() $"[OMOD-APPLY-ENTRY] ctx={formTypeContext} shapes={shapeCount} directProps={directCount} omods={omodCount}")
        End If

        ' (1) DirectProperties of applied combinations — walked in the order ObjectTemplateResolver
        ' produced them (which matches record declaration order).
        For Each prop In res.DirectProperties
            ApplyOneProperty(prop, formTypeContext, shapes, pm, "DIRECT")
        Next

        ' (2) Properties carried by each IncludedOmod, in walk order.
        For Each omod In res.IncludedOmods
            If omod Is Nothing Then Continue For

            ' Skip OMODs whose target FormType doesn't match — vanilla example: OMODs targeting
            ' NONE (workshop wrappers) reached via inventory-side chains. They contribute no
            ' visual change to the actor.
            If omod.FormTypeSignature <> formTypeContext Then
                If logEnabled Then
                    Dim oL = omod
                    Logger.LogLazy(Function() $"[OMOD-APPLY-OMOD-SKIP] omod={oL.EditorID}(0x{oL.FormID:X8}) omodFormType='{oL.FormTypeSignature}' ctx='{formTypeContext}' propsDropped={oL.Properties.Count}")
                End If
                Continue For
            End If

            If logEnabled Then
                Dim oL2 = omod
                Dim propsLen = If(omod.Properties Is Nothing, 0, omod.Properties.Count)
                Dim hasMesh = Not String.IsNullOrEmpty(omod.ModelPath)
                Logger.LogLazy(Function() $"[OMOD-APPLY-OMOD] omod={oL2.EditorID}(0x{oL2.FormID:X8}) hasMesh={hasMesh} props={propsLen}")
            End If

            For Each prop In omod.Properties
                ApplyOneProperty(prop, formTypeContext, shapes, pm, $"OMOD:{omod.FormID:X8}")
            Next
        Next
    End Sub

    ' ───────────────────────────── internals ─────────────────────────────

    Private Sub ApplyOneProperty(prop As OMOD_Property,
                                 formTypeContext As String,
                                 shapes As IEnumerable(Of IRenderableShape),
                                 pm As PluginManager,
                                 sourceTag As String)
        Dim logEnabled = Logger.Enabled
        ' Dispatch by (FormType context, PropertyIdx) — only visual properties produce side-effects.
        Dim dispatched As String = "SKIP"
        Select Case formTypeContext
            Case "ARMO"
                Select Case prop.PropertyIndex
                    Case 12US ' ColorRemappingIndex (Float)
                        dispatched = "CREMAP"
                    Case 13US ' MaterialSwaps (FormID,Int)
                        dispatched = "MSWP"
                End Select
            Case "NPC_"
                Select Case prop.PropertyIndex
                    Case 4US ' ColorRemappingIndex (NPC_) — 0 vanilla cases per dump v2 but supported
                        dispatched = "CREMAP"
                    Case 5US ' MaterialSwaps (FormID,Int)
                        dispatched = "MSWP"
                End Select
                ' WEAP / NONE — out of scope for shape rendering.
        End Select

        If logEnabled Then
            Dim propL = prop
            Dim srcL = sourceTag
            Dim ctxL = formTypeContext
            Dim dispL = dispatched
            Logger.LogLazy(Function() $"[OMOD-APPLY-PROP] src={srcL} ctx={ctxL} idx={propL.PropertyIndex} func={propL.FunctionType} v1=0x{propL.Value1FormID:X8}/v1f={propL.Value1:F3} v2f={propL.Value2:F3} → {dispL}")
        End If

        Select Case dispatched
            Case "CREMAP"
                ApplyColorRemapProp(prop, shapes, sourceTag)
            Case "MSWP"
                ApplyMswpProp(prop, shapes, pm, sourceTag)
        End Select
    End Sub

    Private Sub ApplyMswpProp(prop As OMOD_Property,
                              shapes As IEnumerable(Of IRenderableShape),
                              pm As PluginManager,
                              sourceTag As String)
        Dim mswpFid = prop.Value1FormID
        If mswpFid = 0UI Then Return
        Dim funcType As ShapeMaterialOverrides.MaterialSwapFunction
        Select Case prop.FunctionType
            Case 0 : funcType = ShapeMaterialOverrides.MaterialSwapFunction.SET
            Case 1 : funcType = ShapeMaterialOverrides.MaterialSwapFunction.Remov
            Case 2 : funcType = ShapeMaterialOverrides.MaterialSwapFunction.ADD
            Case Else
                Return
        End Select
        ShapeMaterialOverrides.ApplyMaterialSwap(mswpFid, funcType, shapes, pm)
    End Sub

    Private Sub ApplyColorRemapProp(prop As OMOD_Property,
                                    shapes As IEnumerable(Of IRenderableShape),
                                    sourceTag As String)
        Dim funcType As ShapeMaterialOverrides.ColorRemapFunction
        Select Case prop.FunctionType
            Case 0 : funcType = ShapeMaterialOverrides.ColorRemapFunction.SET
            Case 1 : funcType = ShapeMaterialOverrides.ColorRemapFunction.MUL_ADD
            Case 2 : funcType = ShapeMaterialOverrides.ColorRemapFunction.ADD
            Case Else
                Return
        End Select
        ShapeMaterialOverrides.ApplyColorRemap(prop.Value1, prop.Value2, funcType, shapes)
    End Sub

End Module
