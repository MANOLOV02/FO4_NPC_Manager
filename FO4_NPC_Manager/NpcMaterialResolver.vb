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

''' <summary>Phase 2 of the MainForm split: material / texture-set / hair-palette / color-form
''' resolution extracted from MainForm into a standalone class (DI via NpcRenderContext). Increment 1
''' = resolver/leaf core; ApplyShapeMaterialOverrides + skin-tone resolvers stay in MainForm for
''' later increments. See project_mainform_split.</summary>
Friend NotInheritable Class NpcMaterialResolver
    Private ReadOnly _ctx As NpcRenderContext
    ''' <summary>Overlay resolver injected from MainForm (ApplyPresetOverlayToNpcData) so the
    ''' skin-tone path stays decoupled from MainForm's preset/LM-template machinery.</summary>
    Private ReadOnly _overlayResolver As Func(Of NPC_Data, UInteger, NPC_Data)
    Public Sub New(ctx As NpcRenderContext, overlayResolver As Func(Of NPC_Data, UInteger, NPC_Data))
        _ctx = ctx
        _overlayResolver = overlayResolver
    End Sub

    ' HDPT PNAM Type values (same wbDefinitionsFO4 mapping as MainForm).
    Private Const HeadPartTypeFace As Integer = 1
    Private Const HeadPartTypeHair As Integer = 3
    Private Const HeadPartTypeFacialHair As Integer = 4

    Private Shared _txstFlagDumpDone As Boolean = False

    ''' <summary>Load the body material at <paramref name="materialPath"/> from its VANILLA (BA2) bytes,
    ''' bypassing any loose override. With CBBEHeadRearFix.esp installed the loose ghoulfemalebody.BGSM
    ''' points its diffuse at the CBBE-shaped (CBBE-UV) body; the vanilla-UV nape mesh needs the VANILLA
    ''' body texture instead, so we must read the material that ships in the BA2, not the loose winner.
    '''
    ''' GetOverriddenEntries(materialKey) holds the loser(s) shadowed behind a loose override — the first
    ''' IsLosseFile=False entry is the BA2 material. Parse those bytes with the SAME parser the live
    ''' material chain uses (FO4UnifiedMaterial_Class.Deserialize byte overload, exactly what the normal
    ''' Diccionario overload calls). If there is NO overridden BA2 entry (no loose override present), the
    ''' normal resolver result is already vanilla — fall back to TryLoadMaterialFromDictionary.</summary>
    Friend Shared Function LoadVanillaBodyMaterial(materialPath As String, shape As IRenderableShape) As FO4UnifiedMaterial_Class
        ' Key the override-stack lookup the same way the material chain does: Materials\-prefixed,
        ' separators corrected, lowercased (dictionary is OrdinalIgnoreCase, so case is irrelevant).
        Dim materialKey = FO4UnifiedMaterial_Class.CorrectMaterialPath(materialPath)
        If String.IsNullOrEmpty(materialKey) Then Return Nothing

        Dim materialType As Type
        Select Case IO.Path.GetExtension(materialKey).ToLowerInvariant()
            Case ".bgsm" : materialType = GetType(BGSM)
            Case ".bgem" : materialType = GetType(BGEM)
            Case Else
                ' Unknown extension: defer to the normal resolver (matches MaterialResolver's own
                ' GetMaterialTypeFromPath fallback behaviour).
                Return MaterialResolver.TryLoadMaterialFromDictionary(materialPath, Nothing, shape?.NifShape, shape?.NifContent)
        End Select

        ' Read the material's ARCHIVED (vanilla) bytes directly from the BA2, bypassing _bytesCache.
        ' _bytesCache is keyed by FullPath, which the loose rearfix winner and the BA2 loser share, so a
        ' prior GetBytes() of the loose winner would otherwise be handed back here (377-byte CBBE rearfix
        ' material instead of the 416-byte vanilla one). GetArchiveOriginalBytes never touches that cache.
        Dim vanillaBytes As Byte() = FilesDictionary_class.GetArchiveOriginalBytes(materialKey)

        ' No archived (BA2) entry for this key → the live resolver's winner IS the vanilla content already.
        If vanillaBytes Is Nothing OrElse vanillaBytes.Length = 0 Then
            Logger.LogLazy(Function() $"[DIAG-HEADREAR] vanilla-mat: matKey='{materialKey}' no shadowed BA2 entry → using live resolver (already vanilla)")
            Return MaterialResolver.TryLoadMaterialFromDictionary(materialPath, Nothing, shape?.NifShape, shape?.NifContent)
        End If

        Try
            Dim mat As New FO4UnifiedMaterial_Class()
            mat.Deserialize(vanillaBytes, materialType, shape?.NifShape, shape?.NifContent)
            Logger.LogLazy(Function() $"[DIAG-HEADREAR] vanilla-mat: matKey='{materialKey}' parsed BA2 bytes={vanillaBytes.Length} D='{If(mat.Diffuse_or_Base_Texture, "")}'")
            Return mat
        Catch ex As Exception
            Dim msgL = ex.Message
            Logger.LogLazy(Function() $"[DIAG-HEADREAR] vanilla-mat: matKey='{materialKey}' parse FAILED → {msgL}; falling back to live resolver")
            Return MaterialResolver.TryLoadMaterialFromDictionary(materialPath, Nothing, shape?.NifShape, shape?.NifContent)
        End Try
    End Function

    ''' <summary>Engine-faithful palette/HairTintColor resolution for hair HeadParts. Single source
    ''' of truth — used by BOTH the NIF-load pass (<see cref="ApplyShapeMaterialOverrides"/>) and
    ''' the live face-tint preset refresh (<see cref="RefreshFaceTintLivePreview"/>). Previously
    ''' duplicated in those two sites with subtly different guards; the looser guard at the live
    ''' path leaked hair color into any palette-enabled material (robot armor, face shapes with
    ''' palette opt-in, etc.). This helper enforces the engine rule once.
    '''
    ''' Engine rule: <c>CLFM.RemappingIndex</c> is consumed only by HDPTs that the engine equips
    ''' with a NPC color form. That's Hair (3) / FacialHair (4) / Brow (6) via NPC.HNAM / NPC.QNAM.
    ''' Other HeadParts (Face / Eyes / HeadRear / Meatcaps) carry palette in their BGSM but their
    ''' engine-correct paint comes from TETI SkinTone or the FaceTintCompositor, not from this path.
    ''' Misc (0) deferred — open question whether some Misc parts legitimately need hair color.
    '''
    ''' <para>Behavior per resolved <c>hairColorFormID</c>:
    ''' <list type="number">
    ''' <item>If CLFM has RemappingIndex AND a palette LUT path is resolvable (BGSM-first, RACE.HNAM
    '''   fallback): set <c>GrayscaleToPaletteColor=True</c>, <c>GrayscaleToPaletteScale=clfm.RemappingIndex</c>,
    '''   <c>GreyscaleTexture=palTex</c>.</item>
    ''' <item>Else: fall back to <c>HairTintColor</c>. Caller can pre-resolve a richer tint (NIF-load
    '''   passes ResolveHairTintColor with solidTintColor consideration) via
    '''   <paramref name="hairTintColorOverride"/>; if Nothing the helper resolves via
    '''   ResolveColorFormColor on the hair color form.</item>
    ''' </list></para>
    '''
    ''' No-op for: material=Nothing, candidate not IsHairHeadPart, or material that's neither Hair
    ''' shader nor palette opt-in. Silent (no warning logs) — those are expected for the vast
    ''' majority of shapes; the diagnostic only fires when the helper actually mutates state.
    ''' </summary>
    Friend Sub ApplyMaterialPaletteHairColor(material As FO4UnifiedMaterial_Class,
                                             candidate As MainForm.MeshCandidate,
                                             state As MainForm.NPCVisualState,
                                             hairTintColorOverride As Nullable(Of Color))
        If material Is Nothing Then Return
        If Not IsHairHeadPart(candidate) Then Return
        If Not (material.Hair OrElse material.GrayscaleToPaletteColor) Then Return

        Dim logEnabled = Logger.Enabled
        ' Hair/FacialHair/Brow all read NPC.HCLF. NPC.BCLF is preserved in the ESP for
        ' round-trip (Save ESP writes raw BCLF untouched) but ignored at render/bake time:
        ' F4SE/LooksMenu in-game also only reads headData->hairColor (CharGenInterface.cpp
        ' ProcessHairColor), and a workspace audit found BCLF used by 5/4473 NPCs total
        ' (all from one CC pack, 4 redundant with HCLF). Unifying on HCLF aligns with the
        ' in-game runtime the user actually sees.
        Dim hairColorFormID As UInteger = If(state IsNot Nothing, state.HairColorFormID, 0UI)

        Dim didPalette As Boolean = False
        If hairColorFormID <> 0UI Then
            Dim clfm = ResolveColorFormData(hairColorFormID)
            If clfm IsNot Nothing AndAlso clfm.HasRemappingIndex Then
                ' PRESERVAR el opt-in de palette de la FUENTE (no forzarlo). Probado sobre el corpus
                ' FaceGen vanilla (BeardRuleProbe 2026-06-13, 1100 shapes de barba, 7 diffuse): el flag
                ' GreyscaleToPalette_Color es UNIFORME por barba (función de la barba fuente, NO del NPC,
                ' 0 casos mix). CK lo deja como vino la fuente: barbas tintables (facialhair01/02, haircurly*)
                ' con flag ON; stubble (hairshaved04) con flag OFF. Nuestro código forzaba ON para toda
                ' shape Hair → rompía las OFF (88/1100). Fix: solo encender el flag + inyectar la textura
                ' del LUT si la FUENTE ya optó por palette (flag propio o textura greyscale propia).
                ' El SCALE (RemappingIndex) se escribe SIEMPRE — CK lo propaga uniforme por NPC, inerte
                ' en las shapes sin flag/textura (memoria grayscale 2026-05-25).
                Dim sourceHadPalette As Boolean = material.GrayscaleToPaletteColor OrElse Not String.IsNullOrEmpty(material.GreyscaleTexture)
                Dim oldPalColor = material.GrayscaleToPaletteColor
                Dim oldScale = material.GrayscaleToPaletteScale
                Dim oldGreyTex = If(logEnabled, If(material.GreyscaleTexture, ""), Nothing)
                material.GrayscaleToPaletteScale = clfm.RemappingIndex
                Dim palTex As String = ""
                If sourceHadPalette Then
                    ' Priority: BGSM's own GreyscaleTexture first (per-shape, picked by the stylist
                    ' for THIS mesh), RACE.HNAM/HLTX as fallback. The engine in-game binds the LUT
                    ' from the material's TXST slot 3 at render time (F4SE CharGenInterface.cpp:
                    ' 1106-1179, ProcessHairColor → SetTextureFilename(3, ...)). Vanilla
                    ' HumanChildRace ships without HNAM/HLTX precisely because the BGSM carries it.
                    palTex = If(material.GreyscaleTexture, "")
                    If palTex = "" Then palTex = ResolveRaceHairLookupTexture(state, _ctx.PluginManager)
                    If palTex <> "" Then
                        material.GrayscaleToPaletteColor = True
                        material.GreyscaleTexture = palTex
                    End If
                End If
                ' La rama palette manejó el material (escribió el scale) → no caer al HairTintColor
                ' fallback, que pisaría el HairTintColor de la fuente (CK no lo cambia en barbas OFF).
                didPalette = True
                If logEnabled Then
                    Dim newScale = clfm.RemappingIndex
                    Dim hairFidL = hairColorFormID
                    Dim palTexL = palTex
                    Dim srcHad = sourceHadPalette
                    Dim newPal = material.GrayscaleToPaletteColor
                    Logger.LogLazy(Function() $"[PALSCALE-WRITE] branch=Hair-CLFM hdptType={candidate.HeadPartType} hairColorFid=0x{hairFidL:X8} sourceHadPalette={srcHad} oldPalColor={oldPalColor} oldScale={oldScale:F4} oldGreyTex='{oldGreyTex}' → newPalColor={newPal} newScale={newScale:F4} newGreyTex='{palTexL}'")
                End If
            End If
        End If

        If Not didPalette Then
            Dim effectiveHairColor = hairTintColorOverride
            If Not effectiveHairColor.HasValue AndAlso hairColorFormID <> 0UI Then
                effectiveHairColor = ResolveColorFormColor(hairColorFormID)
            End If
            If effectiveHairColor.HasValue Then
                Dim oldHairCol = material.HairTintColor
                material.HairTintColor = effectiveHairColor.Value
                If logEnabled Then
                    Dim newColLog = effectiveHairColor.Value
                    Logger.LogLazy(Function() $"[HAIRTINT-WRITE] hdptType={candidate.HeadPartType} oldRGB=({oldHairCol.R},{oldHairCol.G},{oldHairCol.B}) → newRGB=({newColLog.R},{newColLog.G},{newColLog.B})")
                End If
            End If
        End If
    End Sub

    ''' <summary>Resuelve el TXST del body skin del actor (NPC.WNAM o RACE.WNAM via state.SkinFormID),
    ''' diferenciando por región: BODY (torso/legs) o HAND. El engine in-game sustituye la diffuse
    ''' texture de los shapes con BSLightingShaderType.SkinTint por la del actor — esto permite a
    ''' un mismo .nif outfit (autoreado con texturas embebidas humanas) verse correcto sobre ghoul,
    ''' synth, super mutant, etc. La sustitución debe usar la textura body (NakedTorso ARMA) para
    ''' shapes con piel del torso/brazos/legs y la hand (NakedHands ARMA) para shapes en gloves
    ''' con piel expuesta de manos.
    ''' Retorna Nothing si state.SkinFormID no resuelve a un ARMO con ARMA gender-correct válida.</summary>
    Friend Function ResolveActorSkinTextureSet(state As MainForm.NPCVisualState, region As MainForm.SkinRegion) As TXST_Data
        If state Is Nothing OrElse state.SkinFormID = 0UI Then Return Nothing

        Dim armo = _ctx.GetParsedArmo(state.SkinFormID)
        If armo Is Nothing Then Return Nothing

        Const BODY_BIT As UInteger = 1UI << 3
        Const HAND_MASK As UInteger = (1UI << 4) Or (1UI << 5)

        ' Iterar las ARMAs del Skin ARMO; elegir la que cubra la región pedida.
        For Each entry In armo.ArmorAddons
            Dim arma = _ctx.GetParsedArma(entry.ArmaFormID)
            If arma Is Nothing Then Continue For
            Dim armaSlot = arma.SlotMask

            Dim matches As Boolean = False
            Select Case region
                Case MainForm.SkinRegion.Body
                    matches = (armaSlot And BODY_BIT) <> 0UI
                Case MainForm.SkinRegion.Hand
                    matches = (armaSlot And HAND_MASK) <> 0UI AndAlso (armaSlot And BODY_BIT) = 0UI
            End Select
            If Not matches Then Continue For

            Dim txstFID = If(state.IsFemale,
                             If(arma.FemaleSkinTextureFormID <> 0UI, arma.FemaleSkinTextureFormID, arma.MaleSkinTextureFormID),
                             If(arma.MaleSkinTextureFormID <> 0UI, arma.MaleSkinTextureFormID, arma.FemaleSkinTextureFormID))
            If txstFID = 0UI Then Continue For

            Dim txstRec = _ctx.PluginManager.GetRecord(txstFID)
            If txstRec Is Nothing OrElse txstRec.Header.Signature <> "TXST" Then Continue For

            Return RecordParsers.ParseTXST(txstRec, _ctx.PluginManager)
        Next

        Return Nothing
    End Function

    ''' <summary>Decide qué región de skin (Body vs Hand) corresponde a un Outfit candidate según
    ''' su SlotMask. Outfits tipo "MOutfit/FOutfit" (cubren BODY+[U]) → Body; gloves outfits (sólo
    ''' bits hand sin BODY/[U]) → Hand. Para [A] over-armor con piel expuesta (raro), el slot
    ''' indica qué cubre — si toca BODY/[U] usar Body; si sólo [A]/hand → Hand.</summary>
    Friend Shared Function ResolveSkinRegionForOutfit(candidate As MainForm.MeshCandidate) As MainForm.SkinRegion
        If candidate Is Nothing Then Return MainForm.SkinRegion.Body
        Const BODY_BIT As UInteger = 1UI << 3
        Const HAND_MASK As UInteger = (1UI << 4) Or (1UI << 5)
        Dim U_MASK As UInteger = 0UI
        For b = 6 To 10 : U_MASK = U_MASK Or (1UI << b) : Next

        Dim slot = candidate.SlotMask
        Dim touchesBodyOrU = (slot And BODY_BIT) <> 0UI OrElse (slot And U_MASK) <> 0UI
        Dim touchesHand = (slot And HAND_MASK) <> 0UI

        ' Body/[U] tiene precedencia sobre hand: outfits tipo "all-in-one" con BODY+hands
        ' (ej. AAClothesCait slot 33+34+35) usan body skin para la zona de torso/brazos.
        If touchesBodyOrU Then Return MainForm.SkinRegion.Body
        If touchesHand Then Return MainForm.SkinRegion.Hand
        Return MainForm.SkinRegion.Body  ' default seguro: si no toca nada conocido (raro), body.
    End Function

    Friend Function ResolveHeadPartSolidTintColor(candidate As MainForm.MeshCandidate) As Nullable(Of Color)
        If candidate Is Nothing OrElse Not candidate.UseSolidTint Then Return Nothing
        Return ResolveColorFormColor(candidate.HeadPartColorFormID)
    End Function

    Friend Function ResolveTextureSet(candidate As MainForm.MeshCandidate, state As MainForm.NPCVisualState) As TXST_Data
        Dim logEnabled = Logger.Enabled
        ' Regla canónica HeadPart TXST resolution (per HDPT.DATA flags spec
        ' wbDefinitionsFO4.pas:7365-7372):
        '   A) sin TNAM, sin UsesBodyTexture → Nothing (deja lo embebido del NIF).
        '   B) con TNAM, sin UsesBodyTexture → usa TNAM (lo que el HDPT trae).
        '   C) UsesBodyTexture=True → body TXST del actor (state.SkinFormID → NakedTorso ARMA →
        '      Male/FemaleTxst gender-correct). La cadena SkinFormID es race-specific, así un mismo
        '      HDPT compartido entre razas (RNAM=FLST con Human+Ghoul, ej. FemaleHeadHumanRearTEMP)
        '      renderiza con texturas distintas según la raza del NPC.
        ' Caso particular Face: si un HDPT cuyo *raw* PartType=Face no tiene TNAM, fallback a
        ' state.HeadTextureFormID (NPC.FTST). Esto cubre HDPTs Face vanilla que dependen del
        ' FTST per-NPC (ej. NPCs con makeup pre-bakeado en el FTST). IMPORTANTE: usa
        ' HeadPartTypeRaw (no HeadPartType=effective) — sub-parts Misc cuyo effective se
        ' hereda como Face vía HNAM-parent (MouthShadowFemale, eye lashes/AO/wet) NO deben
        ' tomar el FTST del head, lo que les pisaba el Diffuse del shader source con
        ' basefemalehead_d.dds en vez de su propio path autoreado. CK al bakear respeta el
        ' material original de esos sub-parts; verificado contra Alijo vanilla.
        ' Esta regla aplica SÓLO a HeadPart. Skin/Outfit candidates conservan su propio flujo.
        If candidate IsNot Nothing AndAlso candidate.Kind = MainForm.MeshCandidateKind.HeadPart Then
            ' Caso C: UsesBodyTexture=True gana sobre TNAM.
            If candidate.UsesBodyTexture AndAlso state IsNot Nothing Then
                Dim bodyTxst = ResolveActorSkinTextureSet(state, MainForm.SkinRegion.Body)
                If bodyTxst IsNot Nothing Then
                    If logEnabled Then
                        Dim bFid = bodyTxst.FormID, bMnam = If(bodyTxst.MaterialPath, "")
                        Dim bD = If(bodyTxst.DiffuseTexture, ""), bN = If(bodyTxst.NormalTexture, ""), bS = If(bodyTxst.SmoothSpecTexture, "")
                        Logger.LogLazy(Function() $"[TXST-RESOLVE] source=BodySkin(UsesBodyTexture) txst=0x{bFid:X8} mnam='{bMnam}' D='{bD}' N='{bN}' S='{bS}'")
                    End If
                    Return bodyTxst
                End If
                ' Fallthrough si el actor no tiene body skin resuelto (raro): seguir con TNAM/Face.
            End If
        End If

        Dim textureSetFormID As UInteger = 0UI
        Dim txstSource As String = "none"

        If candidate IsNot Nothing Then
            textureSetFormID = candidate.TextureSetFormID
            If textureSetFormID <> 0UI Then txstSource = "HDPT.TNAM"
            ' Precedencia de la textura base para Face head parts: FTST (propio del NPC) > HDPT.TNAM > DFTM (default
            ' de la raza). El FTST PROPIO (state.ExplicitHeadTextureFormID, capturado ANTES del fallback DFTM en
            ' BuildNPCVisualState) REEMPLAZA el TNAM — la cara declarada del NPC gana sobre el skin default del HDPT
            ' (ej. Mitch FTST=SkinHeadMayor pisa MaleHeadHuman.TNAM=SkinHeadHeroMale). Si no hay FTST propio, queda el
            ' TNAM del head part. Sólo si tampoco hay TNAM se cae a DFTM (state.HeadTextureFormID = DFTM cuando no hay
            ' FTST propio, llenado en :7584). Guard raw=Face (HeadPartTypeRaw, NO effective) protege sub-parts Misc
            ' heredados como Face (MouthShadow/AO/lashes/wet) que conservan su propio material. (Antes:
            ' state.HeadTextureFormID=FTST-o-DFTM pisaba el TNAM -> DFTM le ganaba a TNAM en razas con DFTM<>TNAM; mal.)
            If candidate.Kind = MainForm.MeshCandidateKind.HeadPart AndAlso candidate.HeadPartTypeRaw = HeadPartTypeFace AndAlso state IsNot Nothing Then
                If state.ExplicitHeadTextureFormID <> 0UI Then
                    textureSetFormID = state.ExplicitHeadTextureFormID
                    txstSource = "NPC.FTST(Face-override)"
                ElseIf textureSetFormID = 0UI AndAlso state.HeadTextureFormID <> 0UI Then
                    textureSetFormID = state.HeadTextureFormID
                    txstSource = "RACE.DFTM(Face-fallback)"
                End If
            End If
        End If

        If textureSetFormID = 0UI Then Return Nothing

        Dim rec = _ctx.PluginManager.GetRecord(textureSetFormID)
        If rec Is Nothing OrElse rec.Header.Signature <> "TXST" Then
            If logEnabled Then
                Dim fidL = textureSetFormID, srcL = txstSource
                Logger.LogLazy(Function() $"[TXST-RESOLVE] source={srcL} formID=0x{fidL:X8} → NOT-FOUND-or-not-TXST")
            End If
            Return Nothing
        End If

        Dim parsed = RecordParsers.ParseTXST(rec, _ctx.PluginManager)
        If logEnabled Then
            Dim srcL2 = txstSource, pEid = If(parsed.EditorID, ""), pMnam = If(parsed.MaterialPath, "")
            Dim pD = If(parsed.DiffuseTexture, ""), pN = If(parsed.NormalTexture, ""), pS = If(parsed.SmoothSpecTexture, ""), pW = If(parsed.WrinklesTexture, "")
            ' DNAM flags (wbDefinitionsFO4.pas:7350): 0x0001 NoSpecularMap, 0x0002 FacegenTextures, 0x0004 HasModelSpaceNormal.
            ' Hipótesis: 'FacegenTextures' (0x0002) marca el set de complexión (full D/N/S en el bake) vs TXST normal.
            Dim pFlags = parsed.Flags
            Dim pFacegen = (pFlags And &H2US) <> 0US, pNoSpec = (pFlags And &H1US) <> 0US, pMsn = (pFlags And &H4US) <> 0US
            Logger.LogLazy(Function() $"[TXST-RESOLVE] source={srcL2} txst=0x{parsed.FormID:X8} eid='{pEid}' flags=0x{pFlags:X4}(facegen={pFacegen},noSpec={pNoSpec},msn={pMsn}) mnam='{pMnam}' D='{pD}' N='{pN}' S='{pS}' W='{pW}'")
        End If
        Return parsed
    End Function

    ''' <summary>Pisa los paths de texturas del material con los del TXST (D / N / W / Glow /
    ''' Height / Env / Multilayer / Spec). Si el TXST trae un .bgsm/.bgem en MaterialPath,
    ''' carga ese material y reemplaza el del shape. <c>Friend Shared</c> para que
    ''' HeadPartPicker_Form pueda reutilizarlo en su preview de HDPT.</summary>
    Friend Shared Sub ApplyTextureSetOverrides(textureSet As TXST_Data, relatedMaterial As Nifcontent_Class_Manolo.RelatedMaterial_Class, usesBodyTexture As Boolean, shap As NiflySharp.INiShape, nif As Nifcontent_Class_Manolo, Optional isHeadPartTextureSet As Boolean = False, Optional isFaceHeadPart As Boolean = False)
        If textureSet Is Nothing OrElse relatedMaterial Is Nothing Then Return

        Dim logEnabled = Logger.Enabled
        Dim material = relatedMaterial.material
        If material Is Nothing Then Return

        ' MNAM-loaded rule (split by HDPT.UsesBodyTexture, verified empirically vs CK bake):
        '   - UsesBodyTexture=True : full-replace. The HDPT declares "this part wears the
        '     body skin" so the MNAM-pointed BGSM is the body-skin material in its entirety;
        '     D + N + S + everything else come from the override. Verified vs Alice
        '     ChildHeadRear (vanilla female child, MNAM=childfemalebody.bgsm) and the
        '     Carol-style ghoul HeadRear with CBBE override.
        '   - UsesBodyTexture=False: diffuse-only. The MNAM just supplies the surface tint
        '     for this specific shape; Normal/SmoothSpec/Envmap/shaderType/EnvironmentMapping/
        '     TwoSided all stay from the inline NIF shader. Verified vs Valentine
        '     SynthGen2HeadRearValentine (TXST.MNAM=gen2skindirty.bgsm has type=Default
        '     no-Envmap, but CK bake kept inline type=EnvironmentMap with the Envmap path
        '     and the non-dirty SmoothSpec).
        ' The TXST's TX## slots are layered on top by ApplyTextureSetToMaterial below, so any
        ' slot the TXST explicitly sets still wins regardless of the branch above.
        If textureSet.MaterialPath <> "" Then
            Dim overrideMaterial = MaterialResolver.TryLoadMaterialFromDictionary(textureSet.MaterialPath, material, shap, nif)
            If overrideMaterial IsNot Nothing Then
                ' TEXTURES-ONLY + ALPHA (2026-06-15): el MNAM del TXST aporta SOLO sus paths de textura
                ' MÁS el alpha (AlphaTest/AlphaBlend) verbatim. El resto del shader (ShaderType/
                ' SubsurfaceRolloff/BackLight/Smoothness/Specular/flags) queda del clon del mesh FUENTE —
                ' el .bgsm es el material runtime del engine, CK nunca lo hornea en el shader del FaceGen
                ' NIF. Verificado por identidad contra los 10.197 shapes de CK: donde hay MNAM, o es
                ' FaceGen=True (CK=shader del fuente) o FaceGen=False con source==material; ninguna shape
                ' bakeada necesita el shader del material. Reemplaza el experimento full-replace y el
                ' viejo gate usesBodyTexture.
                ' El alpha SÍ se toma del material override (no era así en la versión 06-14 textures-only):
                ' CK emite un NiAlphaProperty gobernado por el alpha del material de cabeza (p.ej.
                ' Gen2SkinHeadValentine.BGSM AlphaTest=True/Ref=128/Blend=Standard) y sin esto el NIF
                ' bakeado perdía el NiAlphaProperty y el flag SF2 Alpha_Test. Decisión de auditoría.
                ' Ver reference_facegen_ck_must_come_from_ba2.
                material.Diffuse_or_Base_Texture = overrideMaterial.Diffuse_or_Base_Texture
                material.NormalTexture = overrideMaterial.NormalTexture
                material.SmoothSpecTexture = overrideMaterial.SmoothSpecTexture
                material.GreyscaleTexture = overrideMaterial.GreyscaleTexture
                material.GlowTexture = overrideMaterial.GlowTexture
                material.WrinklesTexture = overrideMaterial.WrinklesTexture
                material.EnvmapTexture = overrideMaterial.EnvmapTexture
                material.SpecularTexture = overrideMaterial.SpecularTexture
                material.LightingTexture = overrideMaterial.LightingTexture
                material.FlowTexture = overrideMaterial.FlowTexture
                material.InnerLayerTexture = overrideMaterial.InnerLayerTexture
                material.DisplacementTexture = overrideMaterial.DisplacementTexture
                ' Alpha (AlphaTest/AlphaBlend) del material override SÓLO para el head part de cara
                ' (PartType=Face). CK emite el NiAlphaProperty gobernado por el alpha del material de
                ' cabeza sólo en synth con reemplazo (Valentine/DiMa). Pelo/barba/neckgore/ojos/mouth
                ' conservan el alpha de su material fuente (= CK) y NO se tocan acá.
                If isFaceHeadPart Then
                    material.AlphaTest = overrideMaterial.AlphaTest
                    material.AlphaTestRef = overrideMaterial.AlphaTestRef
                    material.AlphaBlendMode = overrideMaterial.AlphaBlendMode
                End If
                relatedMaterial.path = FO4UnifiedMaterial_Class.CorrectMaterialPath(textureSet.MaterialPath)
                If logEnabled Then
                    Dim mnamL = If(textureSet.MaterialPath, ""), ubt = usesBodyTexture
                    Logger.LogLazy(Function() $"[TXST-MNAM] mnam='{mnamL}' usesBodyTexture={ubt} → TEXTURES-ONLY (shader del fuente)")
                End If
            End If
        End If

        ApplyTextureSetToMaterial(material, textureSet, isHeadPartTextureSet)
    End Sub

    Friend Shared Sub ApplyTextureSetToMaterial(material As FO4UnifiedMaterial_Class, textureSet As TXST_Data, Optional isHeadPartTextureSet As Boolean = False)
        If material Is Nothing OrElse textureSet Is Nothing Then Return

        Dim logEnabled = Logger.Enabled
        ' Slot override gate — regla confirmada por dump de 997 TXST (2026-05-31, bug Alana/OldHumanFemale).
        ' Por defecto el TXST pisa TODOS los slots que resuelven (D+N+W+Glow+Height+Env+Inner+SmoothSpec).
        ' ÚNICA excepción (diffuse-only): el TextureSet de un HEAD PART SIN el flag DNAM 'Facegen Textures'
        ' (0x0002, xEdit wbDefinitionsFO4.pas:7350). Ese es un swatch per-part (color de ojo/boca): el BGSM
        ' del shape posee N/S/env (ej. ojo vanilla: EyeGloss_n + eyeenvironmentmask_m, que CK conserva).
        '   - Con el flag (complexión/piel SkinHead*/SkinBody*, y mods que lo setean p.ej. TEOB eyes) → full D/N/S.
        '   - Fuera de head-part (body/outfit/armadura) → full (no se aplica la excepción).
        ' Confirmado en dump: TODOS los EyesMaleHuman* vanilla = facegen=False (diffuse-only); todas las
        ' complexiones = facegen=True (full). Reemplaza el viejo gate por mnamEmpty (que descartaba el
        ' Old_n/_s del Face = el bug) y el parche transitorio por match "Eyes".
        Dim isFacegen = (textureSet.Flags And &H2US) <> 0US
        Dim diffuseOnly = isHeadPartTextureSet AndAlso Not isFacegen
        Dim txstFid = textureSet.FormID

        If logEnabled Then
            Dim txstEid = If(textureSet.EditorID, "")
            Dim mnamLog = If(textureSet.MaterialPath, "")
            Dim noSpecL = (textureSet.Flags And &H1US) <> 0US, msnL = (textureSet.Flags And &H4US) <> 0US
            Dim flagsL = textureSet.Flags, hpL = isHeadPartTextureSet, fgL = isFacegen, doL = diffuseOnly
            Logger.LogLazy(Function() $"[TXST-APPLY] txst=0x{txstFid:X8} eid='{txstEid}' flags=0x{flagsL:X4}(facegen={fgL},noSpec={noSpecL},msn={msnL}) headPart={hpL} → diffuseOnly={doL} mnam='{mnamLog}'")
        End If

        ' Diffuse (TX00): nunca se gatea. Resto: se salta solo si diffuseOnly (head-part sin flag Facegen).
        If TxstSlotDecision(txstFid, "Diffuse", textureSet.DiffuseTexture, material.Diffuse_or_Base_Texture, gatedSlot:=False, diffuseOnly:=diffuseOnly) Then material.Diffuse_or_Base_Texture = textureSet.DiffuseTexture
        If TxstSlotDecision(txstFid, "Normal", textureSet.NormalTexture, material.NormalTexture, gatedSlot:=True, diffuseOnly:=diffuseOnly) Then material.NormalTexture = textureSet.NormalTexture
        If TxstSlotDecision(txstFid, "Wrinkles", textureSet.WrinklesTexture, material.WrinklesTexture, gatedSlot:=True, diffuseOnly:=diffuseOnly) Then material.WrinklesTexture = textureSet.WrinklesTexture
        If TxstSlotDecision(txstFid, "Glow", textureSet.GlowTexture, material.GlowTexture, gatedSlot:=True, diffuseOnly:=diffuseOnly) Then material.GlowTexture = textureSet.GlowTexture
        If TxstSlotDecision(txstFid, "Height", textureSet.HeightTexture, material.DisplacementTexture, gatedSlot:=True, diffuseOnly:=diffuseOnly) Then material.DisplacementTexture = textureSet.HeightTexture
        If TxstSlotDecision(txstFid, "Envmap", textureSet.EnvironmentTexture, material.EnvmapTexture, gatedSlot:=True, diffuseOnly:=diffuseOnly) Then material.EnvmapTexture = textureSet.EnvironmentTexture
        If TxstSlotDecision(txstFid, "InnerLayer", textureSet.MultilayerTexture, material.InnerLayerTexture, gatedSlot:=True, diffuseOnly:=diffuseOnly) Then material.InnerLayerTexture = textureSet.MultilayerTexture
        If TxstSlotDecision(txstFid, "SmoothSpec", textureSet.SmoothSpecTexture, material.SmoothSpecTexture, gatedSlot:=True, diffuseOnly:=diffuseOnly) Then material.SmoothSpecTexture = textureSet.SmoothSpecTexture
    End Sub

    ''' <summary>DIAGNÓSTICO (2026-05-31): carga un NIF desde el FilesDictionary y loguea el material
    ''' INLINE (shader + texturas, sin overrides TXST/FTST) de cada shape, con el tag
    ''' <c>[NIF-INLINE-MAT]</c>. Sirve para comparar lo que trae el NIF ORIGINAL vs el que NOSOTROS
    ''' redirigimos a <c>_faceBones</c> (CollectHeadPartCandidate), porque el _faceBones puede traer
    ''' un shader/textura distintos al original (ej. HeadRear trae basehumanfemaleskin genérico).
    ''' Todo gateado por <c>Logger.Enabled</c> — carga un NIF de más, solo con logging activo.</summary>
    Friend Shared Sub LogNifInlineMaterials(rawDictKey As String, label As String)
        If Not Logger.Enabled Then Return
        Dim key = NameUtils.NormalizeDictionaryKeyWithMeshesPrefix(rawDictKey)
        Dim loc As FilesDictionary_class.File_Location = Nothing
        If String.IsNullOrEmpty(key) OrElse Not FilesDictionary_class.Dictionary.TryGetValue(key, loc) Then
            Dim kL = key, lblL = label
            Logger.LogLazy(Function() $"[NIF-INLINE-MAT] {lblL} dictKey='{kL}' → NOT-IN-DICT")
            Return
        End If
        Try
            Dim bytes = loc.GetBytes()
            If bytes Is Nothing OrElse bytes.Length = 0 Then Return
            Dim nif As New Nifcontent_Class_Manolo()
            nif.Load_Manolo(bytes)
            Dim shapes = NifRenderableShape.FromNif(nif)
            If shapes Is Nothing Then Return
            For Each shape In shapes
                MaterialResolver.EnsureShapeMaterialResolved(shape)
                Dim rm = shape.ShapeMaterial
                Dim snL = shape.ShapeName, keyL = key, lblL = label
                If rm Is Nothing OrElse rm.material Is Nothing Then
                    Logger.LogLazy(Function() $"[NIF-INLINE-MAT] {lblL} dictKey='{keyL}' shape='{snL}' → no-material")
                    Continue For
                End If
                Dim m = rm.material
                Dim shdr = m.NifShaderType.ToString(), isBgsm = m.IsBGSM(), pathL = If(rm.path, "")
                Dim d = If(m.Diffuse_or_Base_Texture, ""), n = If(m.NormalTexture, ""), s = If(m.SmoothSpecTexture, "")
                Dim sp = If(m.SpecularTexture, ""), w = If(m.WrinklesTexture, ""), env = If(m.EnvmapTexture, "")
                Logger.LogLazy(Function() $"[NIF-INLINE-MAT] {lblL} dictKey='{keyL}' shape='{snL}' shader={shdr} isBGSM={isBgsm} matPath='{pathL}' D='{d}' N='{n}' S='{s}' spec='{sp}' W='{w}' env='{env}'")
            Next
        Catch ex As Exception
            Dim msgL = ex.Message, lblL = label, keyL = key
            Logger.LogLazy(Function() $"[NIF-INLINE-MAT] {lblL} dictKey='{keyL}' → EX: {msgL}")
        End Try
    End Sub

    ''' <summary>DIAGNÓSTICO one-shot (2026-05-31): dumpea TODOS los TXST cargados (vanilla + mods) con
    ''' su flag DNAM (0x0001 NoSpecularMap, 0x0002 FacegenTextures, 0x0004 ModelSpaceNormal) y qué
    ''' slots traen (D/N/S). Sirve para auditar el universo del gate: facegen=True → full D/N/S;
    ''' facegen=False → diffuse-only (skip N/S). Gateado por Logger.Enabled, corre UNA vez por sesión.
    ''' Tag [TXST-DUMP]. Puede ser ruidoso (miles de TXST en el load order) — filtrar por 'facegen='.</summary>
    Friend Sub DumpAllTxstFlagsOnce()
        If Not Logger.Enabled OrElse _txstFlagDumpDone Then Return
        _txstFlagDumpDone = True
        If _ctx.PluginManager Is Nothing Then Return
        Dim list As List(Of PluginRecord) = Nothing
        If Not _ctx.PluginManager.RecordsByType.TryGetValue("TXST", list) OrElse list Is Nothing Then
            Logger.LogLazy(Function() "[TXST-DUMP] no hay TXST en RecordsByType")
            Return
        End If
        Dim total = list.Count, facegenCount = 0
        For Each rec In list
            Dim t = RecordParsers.ParseTXST(rec, _ctx.PluginManager)
            Dim fg = (t.Flags And &H2US) <> 0US
            If fg Then facegenCount += 1
            Dim ns = (t.Flags And &H1US) <> 0US, ms = (t.Flags And &H4US) <> 0US
            Dim fid = t.FormID, eid = If(t.EditorID, ""), fl = t.Flags
            Dim hasD = Not String.IsNullOrEmpty(t.DiffuseTexture)
            Dim hasN = Not String.IsNullOrEmpty(t.NormalTexture)
            Dim hasS = Not String.IsNullOrEmpty(t.SmoothSpecTexture)
            Dim src = If(rec.SourcePluginName, "")
            Logger.LogLazy(Function() $"[TXST-DUMP] 0x{fid:X8} '{eid}' flags=0x{fl:X4} facegen={fg} noSpec={ns} msn={ms} D={hasD} N={hasN} S={hasS} plugin='{src}'")
        Next
        Dim totL = total, fgL = facegenCount
        Logger.LogLazy(Function() $"[TXST-DUMP] === total={totL} facegen={fgL} (full D/N/S) / no-facegen={totL - fgL} (diffuse-only en el gate) ===")
    End Sub

    ''' <summary>Decide si un slot TX0n del TXST pisa al material y loguea la decisión (tag
    ''' <c>[TXST-SLOT]</c>) — incluido el motivo del SKIP. Por defecto aplica si el path resuelve en
    ''' FilesDictionary; el ÚNICO skip es <paramref name="gatedSlot"/> (True para todo menos Diffuse)
    ''' AndAlso <paramref name="diffuseOnly"/> (head-part sin flag 'Facegen Textures'). Ver
    ''' ApplyTextureSetToMaterial para la regla completa.</summary>
    Friend Shared Function TxstSlotDecision(txstFid As UInteger, label As String, txstPath As String,
                                             currentValue As String, gatedSlot As Boolean, diffuseOnly As Boolean) As Boolean
        Dim hasPath = Not String.IsNullOrEmpty(txstPath)
        Dim resolves = TxstSlotResolves(txstPath, label, currentValue)
        Dim blocked = gatedSlot AndAlso diffuseOnly
        Dim apply = resolves AndAlso Not blocked
        If Logger.Enabled Then
            Dim reason As String
            If apply Then
                reason = "APPLY"
            ElseIf Not hasPath Then
                reason = "skip:empty-path"
            ElseIf blocked Then
                reason = "skip:HEADPART-DIFFUSE-ONLY"
            ElseIf Not resolves Then
                reason = "skip:unresolved-in-dict"
            Else
                reason = "skip:unknown"
            End If

            Dim pathL = If(txstPath, ""), keptL = If(currentValue, ""), reasonL = reason
            Dim resolvesL = resolves, doL = diffuseOnly, gsL = gatedSlot
            Logger.LogLazy(Function() $"[TXST-SLOT] txst=0x{txstFid:X8} slot={label} txstPath='{pathL}' resolves={resolvesL} gatedSlot={gsL} diffuseOnly={doL} → {reasonL} (kept='{keptL}')")
        End If
        Return apply
    End Function

    ''' <summary>True when a TXST TX0n path is non-empty AND its file exists in the
    ''' FilesDictionary (BA2 / loose pool). Logs a one-line drop trace when the path is
    ''' set but unresolvable, so empirical rule confirmation stays visible in the log.</summary>
    Friend Shared Function TxstSlotResolves(txstPath As String, slotLabel As String, currentSlotValue As String) As Boolean
        If String.IsNullOrEmpty(txstPath) Then Return False
        Dim normalized = FO4UnifiedMaterial_Class.CorrectTexturePath(txstPath)
        If String.IsNullOrEmpty(normalized) Then Return False
        If FilesDictionary_class.Dictionary.ContainsKey(normalized) Then Return True
        Dim keptValue = If(currentSlotValue, "")
        Return False
    End Function

    Friend Function ResolveHairTintColor(candidate As MainForm.MeshCandidate, state As MainForm.NPCVisualState, headPartColor As Nullable(Of Color)) As Nullable(Of Color)
        ' Hair/FacialHair/Brow all read NPC.HCLF (see ApplyMaterialPaletteHairColor for the
        ' rationale: BCLF ignored at render/bake, preserved untouched in the ESP).
        Select Case candidate.HeadPartType
            Case HeadPartTypeHair, HeadPartTypeFacialHair, 6
                Dim hairColor = ResolveColorFormColor(state.HairColorFormID)
                If hairColor.HasValue Then Return hairColor
        End Select

        If headPartColor.HasValue Then Return headPartColor
        Return Nothing
    End Function

    ''' <summary>Resolve the effective hair palette texture path for a given host + state, using
    ''' the BGSM-first / RACE-fallback rule the renderer applies. Single source of truth so the
    ''' UI swatch, the NIF-load material override, and the live-tint refresh agree. Returns
    ''' "" when no palette is available from any source.
    ''' <para>Priority:</para>
    ''' <list>
    ''' <item>Walk the host's loaded HAIR shapes (mat.Hair only — NOT every g2p material, which
    '''       would also match recolourable armor) and return the first non-empty
    '''       <c>material.GreyscaleTexture</c>. Per-shape, authored by the stylist, matches what
    '''       the engine binds at TXST slot 3.</item>
    ''' <item>Otherwise fall back to <see cref="ResolveRaceHairLookupTexture"/> (RACE.HNAM/HLTX).
    '''       Vanilla HumanRace declares HNAM and most hair BGSMs duplicate it, but
    '''       HumanChildRace ships without HNAM/HLTX so we must rely on the BGSM there.</item>
    ''' </list></summary>
    Friend Shared Function ResolveHairPaletteTexture(host As NpcRenderHost, state As MainForm.NPCVisualState, pluginManager As PluginManager) As String
        If host IsNot Nothing AndAlso host.PreviewCtl IsNot Nothing _
           AndAlso host.PreviewCtl.Model IsNot Nothing AndAlso host.PreviewCtl.Model.meshes IsNot Nothing Then
            For Each mesh In host.PreviewCtl.Model.meshes
                If mesh Is Nothing OrElse mesh.MeshData Is Nothing OrElse mesh.MeshData.Material Is Nothing Then Continue For
                Dim mb = mesh.MeshData.Material.MaterialBase
                If mb Is Nothing Then Continue For
                ' Require a REAL hair material (BGSM Hair flag). The old test
                ' "mb.Hair OrElse mb.GrayscaleToPaletteColor" also matched recolourable ARMOR
                ' (GrayscaleToPaletteColor=True, Hair=False): when the NPC wore e.g. combat armor,
                ' its palette (CombatArmor_palette_d) preceded the hair shape in the mesh list and
                ' was returned as the brow LUT instead of the hair colour LUT (HairColor_*_d). That
                ' was the root cause of the wrong / load-order-"unstable" brow palette. Armor has
                ' Hair=False, so this filter excludes it; bald NPCs fall through to RACE HNAM/HLTX.
                If Not mb.Hair Then Continue For
                Dim gtex = If(mb.GreyscaleTexture, "")
                If gtex <> "" Then Return gtex
            Next
        End If
        Return ResolveRaceHairLookupTexture(state, pluginManager)
    End Function

    Friend Shared Function ResolveRaceHairLookupTexture(state As MainForm.NPCVisualState, pluginManager As PluginManager) As String
        If state Is Nothing OrElse state.RaceFormID = 0UI OrElse pluginManager Is Nothing Then Return ""

        Dim raceRec = pluginManager.GetRecord(state.RaceFormID)
        If raceRec Is Nothing OrElse raceRec.Header.Signature <> "RACE" Then Return ""

        Dim race = RecordParsers.ParseRACE(raceRec, pluginManager)
        If race Is Nothing Then Return ""

        Dim lookupCandidates = New String() {race.HairColorLookupTexture, race.HairColorExtendedLookupTexture}
        For Each lookupTexture In lookupCandidates
            Dim correctedPath = FO4UnifiedMaterial_Class.CorrectTexturePath(lookupTexture)
            If correctedPath <> "" AndAlso FilesDictionary_class.Dictionary.ContainsKey(correctedPath) Then
                Return lookupTexture
            End If
        Next

        For Each lookupTexture In lookupCandidates
            If Not String.IsNullOrWhiteSpace(lookupTexture) Then Return lookupTexture
        Next

        Return ""
    End Function

    Friend Shared Function IsHairHeadPart(candidate As MainForm.MeshCandidate) As Boolean
        If candidate Is Nothing OrElse candidate.Kind <> MainForm.MeshCandidateKind.HeadPart Then Return False
        ' Hair (3), Facial Hair (4), Hairline/Brow (6) all use hair color
        Return candidate.HeadPartType = HeadPartTypeHair OrElse
               candidate.HeadPartType = HeadPartTypeFacialHair OrElse
               candidate.HeadPartType = 6
    End Function

    Friend Function ResolveColorFormColor(formID As UInteger) As Nullable(Of Color)
        Dim clfm = ResolveColorFormData(formID)
        If clfm Is Nothing OrElse Not clfm.HasColor Then Return Nothing
        Return clfm.Color
    End Function

    Friend Function ResolveColorFormData(formID As UInteger) As CLFM_Data
        If formID = 0UI Then Return Nothing

        Dim rec = _ctx.PluginManager.GetRecord(formID)
        If rec Is Nothing OrElse rec.Header.Signature <> "CLFM" Then Return Nothing

        Return RecordParsers.ParseCLFM(rec, _ctx.PluginManager)
    End Function

    Friend Function ResolveSkinTintColor(candidate As MainForm.MeshCandidate, state As MainForm.NPCVisualState, headPartColor As Nullable(Of Color)) As Nullable(Of Color)
        ' PRIORITY 1: the NPC's SkinTone tint layer (TETI slot 12).
        ' This is the authoritative source for a character's skin color in FO4 — it's what the engine
        ' uses when applying skin tint. Both my face tint overlay (which skips SkinTone) and the legacy
        ' SkinTintColor multiplier need this value to produce the correct final color.
        If state IsNot Nothing AndAlso candidate IsNot Nothing AndAlso candidate.HeadPartType = HeadPartTypeFace Then
            Dim skinToneColor = ResolveNpcSkinToneColor(state)
            If skinToneColor.HasValue Then Return skinToneColor
        End If

        If state IsNot Nothing AndAlso state.HasTextureLighting Then
            Return state.TextureLightingColor
        End If

        If candidate.HeadPartType = HeadPartTypeFace AndAlso headPartColor.HasValue Then
            Return headPartColor
        End If

        Return Nothing
    End Function

    ''' <summary>Resolve the NPC's effective skin-tone colour and pack it RGB + (tl.Value as
    ''' alpha) — same shape as QNAM RGBA. Body SoftLight reads .A as the opacity factor; the
    ''' face compositor reads tl.Value directly. Both stay in lockstep because they trace back
    ''' to the same source: the layer at the race's SkinTone slot, which is what the engine's
    ''' <c>characterCreation-&gt;skinTint</c> pointer resolves to (verified F4SE
    ''' ScaleformNatives.cpp:860-922).
    ''' <para>The Slot enum value here is a schema-defined field name (xEdit
    ''' wbDefinitionsFO4.pas:3478), not a hardcoded magic number. Returns Nothing when the NPC
    ''' has no layer at the SkinTone slot or the race / CLFM lookup fails.</para></summary>
    Friend Function ResolveNpcSkinToneColor(state As MainForm.NPCVisualState) As Nullable(Of Color)
        If state Is Nothing Then Return Nothing
        Dim modelNpcFormID = NpcStateFactory.FaceAppearanceSourceFormID(state)
        Dim npcData = _overlayResolver(_ctx.GetParsedNpc(modelNpcFormID), state.RootNpcFormID)
        If npcData Is Nothing Then Return Nothing

        Dim raceRec = _ctx.PluginManager.GetRecord(state.RaceFormID)
        If raceRec Is Nothing OrElse raceRec.Header.Signature <> "RACE" Then Return Nothing
        Dim race = _ctx.ParseRaceCached(raceRec)

        ' Single source of truth — same derivation NpcRecordOverlay uses at save time, so the
        ' preview's body skin tone and the persisted ESP's QNAM are guaranteed to agree.
        Return NpcRecordOverlay.DeriveSkinToneQnam(npcData, race, state.IsFemale, _ctx.PluginManager)
    End Function

    ' ===== Ghoul female head-rear (nape) vanilla-UV texture clone =====
    ' Bare-id of FemaleHeadHumanRearTEMP (vanilla 0x0004D0E9, PartType 9). Load-order prefix in the
    ' high byte differs per plugin chain (vanilla Fallout4.esm=0x00, overrides=0x01..0xFF); the
    ' record ID is shared, so all comparisons mask the low 24 bits.
    Private Const FemaleHeadHumanRearTEMPBareID As UInteger = &H4D0E9UI
    Private Const HeadRearEffectivePartType As Integer = 9

    ''' <summary>Bare-id (low-24-bits) set of ghoul-skinned female races. Probe-derived (GhoulMatProbe /
    ''' WetTraceProbe walked the race→skin chain): GhoulRace 0x000EAFB6 and GhoulChildRace 0x0011EB96.
    ''' For these races the female head-rear nape pulls the actor body-skin texture (UsesBodyTexture
    ''' semantics) — see <see cref="ResolveGhoulHeadRearClonedTextures"/> for why a vanilla-bytes
    ''' clone is needed instead of the live (CBBE-overridden) path. Human / all other races: unchanged.</summary>
    Private Shared ReadOnly GhoulFemaleHeadRearRaceBareIDs As New HashSet(Of UInteger) From {
        &HEAFB6UI,   ' GhoulRace
        &H11EB96UI   ' GhoulChildRace
    }

    ' Dedicated loose-file clone root under the game Data dir. Keeps the cloned vanilla bytes under a
    ' DISTINCT path key so the model-wide, path-keyed render texture cache (Render.vb Textures_Dictionary)
    ' can hold them separately from the body's own (same-named) texture.
    Private Const HeadRearClonedTextureRoot As String = "Textures\ManoloCloned\NpcMgrHeadRear\"

    ' Per-app-session memo for ResolveGhoulHeadRearClonedTextures. The cloned-texture result depends
    ' ONLY on the actor body-skin source identity = (RaceFormID, SkinFormID); the vanilla BA2 material +
    ' texture bytes don't change during a session. Without this, every render of a ghoul-female head-rear
    ' (full render, fast-path skin refresh, and bake) re-reads the BA2 BGSM and re-reads the BA2 texture
    ' bytes (length-compare for the idempotent write). Memoizing makes the read+clone happen ONCE per key.
    ' A Nothing result is cached too (deterministic failure shouldn't be retried every render). Session-only
    ' (Shared, no config), reset implicitly on app restart. Renders run on background threads (Task.Run),
    ' so the check-and-compute is held under _ghoulHeadRearMemoLock — the lock spans the compute (a one-time
    ' fast op per key) so concurrent renders for the SAME key don't duplicate the clone write / FilesDictionary
    ' registration. No toggle invalidation needed: the clone result is identical regardless of
    ' ApplyGhoulHeadRearFix (the toggle only gates whether the clone is APPLIED, upstream of this resolver).
    Private Shared ReadOnly _ghoulHeadRearMemoLock As New Object()
    Private Shared ReadOnly _ghoulHeadRearClonedTexturesMemo As New Dictionary(Of (RaceFormID As UInteger, SkinFormID As UInteger), (Diffuse As String, Normal As String, SmoothSpec As String)?)()

    ''' <summary>True when this candidate/HDPT is the ghoul-female head-rear case that needs the
    ''' vanilla-UV body texture clone: effective PartType 9 AND HDPT bare-id = FemaleHeadHumanRearTEMP
    ''' AND the actor race ∈ the ghoul-female race set.</summary>
    Friend Shared Function IsGhoulHeadRearCase(hdptFormID As UInteger, effectivePartType As Integer, state As MainForm.NPCVisualState) As Boolean
        If Not NPC_Config.Current.ApplyGhoulHeadRearFix Then Return False
        If effectivePartType <> HeadRearEffectivePartType Then Return False
        If (hdptFormID And &HFFFFFFUI) <> FemaleHeadHumanRearTEMPBareID Then Return False
        If state Is Nothing OrElse Not state.IsFemale Then Return False
        Return GhoulFemaleHeadRearRaceBareIDs.Contains(state.RaceFormID And &HFFFFFFUI)
    End Function

    ''' <summary>For the ghoul-female head-rear, resolve the actor body-skin D/N/S texture paths (the
    ''' SAME paths the body shape uses — via the actor's body skin TXST + its MNAM BGSM), then for each
    ''' produce a DISTINCT loose path under <see cref="HeadRearClonedTextureRoot"/> that contains the
    ''' VANILLA (BA2) bytes of that texture, register it in the FilesDictionary, and return the cloned
    ''' relative paths.
    '''
    ''' Why: the vanilla-UV nape mesh needs a vanilla-UV body texture. With CBBE installed the loose
    ''' file at the body path (e.g. GhoulFemaleBody_d.dds) is CBBE's 4096 CBBE-UV body — UV-mismatched
    ''' against the vanilla-UV nape. The render texture cache is keyed by path string and shared model-wide,
    ''' so the body and the nape can't get different bytes under the same path key. Clone-to-disk gives
    ''' the nape a distinct key holding the vanilla bytes (the body keeps its own path → unaffected).
    '''
    ''' Idempotent: a clone already present with the same byte length is reused (no rewrite).
    ''' Returns Nothing when not applicable (non-ghoul-female, no body skin resolved, or no paths).
    ''' <paramref name="shape"/> supplies the NIF shape/content needed to load the body-skin MNAM BGSM
    ''' exactly the way the live material chain does (mirrors ApplyTextureSetOverrides + ApplyTextureSetToMaterial).</summary>
    Private Function ResolveGhoulHeadRearClonedTextures(state As MainForm.NPCVisualState, shape As IRenderableShape) As (Diffuse As String, Normal As String, SmoothSpec As String)?
        ' Per-session memo: the result depends only on the actor body-skin identity (RaceFormID, SkinFormID).
        ' Hold the lock across the compute so concurrent renders for the same key clone+register only once.
        Dim memoKey = (state.RaceFormID, state.SkinFormID)
        SyncLock _ghoulHeadRearMemoLock
            Dim cached As (Diffuse As String, Normal As String, SmoothSpec As String)? = Nothing
            If _ghoulHeadRearClonedTexturesMemo.TryGetValue(memoKey, cached) Then
                Logger.LogLazy(Function() $"[DIAG-HEADREAR] memo-hit race=0x{memoKey.Item1:X8} skin=0x{memoKey.Item2:X8} → reusing session result")
                Return cached
            End If

            Dim result = ResolveGhoulHeadRearClonedTexturesCompute(state, shape)
            _ghoulHeadRearClonedTexturesMemo(memoKey) = result
            Return result
        End SyncLock
    End Function

    ''' <summary>Compute (no memo) for <see cref="ResolveGhoulHeadRearClonedTextures"/>. Reads the actor
    ''' body-skin TXST, loads the VANILLA (BA2) body material, and clones the vanilla D/N/S texture bytes
    ''' to distinct loose paths. Wrapped by the session memo so this runs once per (race, skin) per session.</summary>
    Private Function ResolveGhoulHeadRearClonedTexturesCompute(state As MainForm.NPCVisualState, shape As IRenderableShape) As (Diffuse As String, Normal As String, SmoothSpec As String)?
        Dim bodyTxst = ResolveActorSkinTextureSet(state, MainForm.SkinRegion.Body)
        If bodyTxst Is Nothing Then
            Logger.LogLazy(Function() $"[DIAG-HEADREAR] resolve: bodyTxst=Nothing (ResolveActorSkinTextureSet Body returned Nothing; state.SkinFormID=0x{If(state IsNot Nothing, state.SkinFormID, 0UI):X8}) → no clone")
            Return Nothing
        End If

        ' Final body D/N/S exactly as the body resolves them: MNAM BGSM (if any) supplies the base
        ' D/N/S, then the TXST's own non-empty TX## slots are layered on top (TX00 diffuse always
        ' wins). Same precedence as ApplyTextureSetOverrides + ApplyTextureSetToMaterial.
        Dim srcD As String = ""
        Dim srcN As String = ""
        Dim srcS As String = ""
        If Not String.IsNullOrEmpty(bodyTxst.MaterialPath) Then
            Dim bodyBgsm = LoadVanillaBodyMaterial(bodyTxst.MaterialPath, shape)
            If bodyBgsm IsNot Nothing Then
                srcD = If(bodyBgsm.Diffuse_or_Base_Texture, "")
                srcN = If(bodyBgsm.NormalTexture, "")
                srcS = If(bodyBgsm.SmoothSpecTexture, "")
            End If
        End If
        If Not String.IsNullOrEmpty(bodyTxst.DiffuseTexture) Then srcD = bodyTxst.DiffuseTexture
        If Not String.IsNullOrEmpty(bodyTxst.NormalTexture) Then srcN = bodyTxst.NormalTexture
        If Not String.IsNullOrEmpty(bodyTxst.SmoothSpecTexture) Then srcS = bodyTxst.SmoothSpecTexture

        Logger.LogLazy(Function() $"[DIAG-HEADREAR] resolve: mnam='{If(bodyTxst.MaterialPath, "")}' srcD='{srcD}' srcN='{srcN}' srcS='{srcS}'")
        Dim clonedD = CloneVanillaTextureToLoose(srcD)
        Dim clonedN = CloneVanillaTextureToLoose(srcN)
        Dim clonedS = CloneVanillaTextureToLoose(srcS)
        Logger.LogLazy(Function() $"[DIAG-HEADREAR] resolve: clonedD='{clonedD}' clonedN='{clonedN}' clonedS='{clonedS}'")

        ' Nothing cloned (no source paths or clone failed) → don't override; caller leaves the shape's
        ' own material untouched.
        If String.IsNullOrEmpty(clonedD) AndAlso String.IsNullOrEmpty(clonedN) AndAlso String.IsNullOrEmpty(clonedS) Then
            Return Nothing
        End If

        Return (clonedD, clonedN, clonedS)
    End Function


    ''' <summary>If <paramref name="candidate"/> is the ghoul-female head-rear case, resolve the
    ''' vanilla-bytes texture clone (see <see cref="ResolveGhoulHeadRearClonedTextures"/>) and overwrite
    ''' the head-rear material's D/N/S with the cloned distinct paths. Single source of truth for the
    ''' full render (via ApplyShapeMaterialOverrides), the fast-path skin refresh, and the FaceGen bake
    ''' (which calls ApplyShapeMaterialOverrides through its delegate, so the baked NIF references the
    ''' persistent vanilla clone — fixes in-game too). No-op for every other shape.</summary>
    Friend Sub ApplyGhoulHeadRearClonedTextures(candidate As MainForm.MeshCandidate, state As MainForm.NPCVisualState,
                                                 material As FO4UnifiedMaterial_Class, shape As IRenderableShape)
        If candidate Is Nothing OrElse material Is Nothing Then Return
        If candidate.Kind <> MainForm.MeshCandidateKind.HeadPart Then Return
        ' [DIAG-HEADREAR] TEMP: trace whenever the head-rear HDPT reaches Apply, pass or fail.
        If Logger.Enabled AndAlso (candidate.HeadPartHdptFormID And &HFFFFFFUI) = FemaleHeadHumanRearTEMPBareID Then
            Dim ptL = candidate.HeadPartType, fidL = candidate.HeadPartHdptFormID
            Dim femL = state IsNot Nothing AndAlso state.IsFemale
            Dim raceL = If(state IsNot Nothing, state.RaceFormID, 0UI)
            Dim togL = NPC_Config.Current.ApplyGhoulHeadRearFix
            Dim gateL = IsGhoulHeadRearCase(candidate.HeadPartHdptFormID, candidate.HeadPartType, state)
            Logger.LogLazy(Function() $"[DIAG-HEADREAR] reached Apply: hdpt=0x{fidL:X8} effType={ptL} female={femL} race=0x{raceL:X8} toggle={togL} gatePass={gateL}")
        End If
        If Not IsGhoulHeadRearCase(candidate.HeadPartHdptFormID, candidate.HeadPartType, state) Then Return

        Dim cloned = ResolveGhoulHeadRearClonedTextures(state, shape)
        If Not cloned.HasValue Then Return

        If Not String.IsNullOrEmpty(cloned.Value.Diffuse) Then material.Diffuse_or_Base_Texture = cloned.Value.Diffuse
        If Not String.IsNullOrEmpty(cloned.Value.Normal) Then material.NormalTexture = cloned.Value.Normal
        If Not String.IsNullOrEmpty(cloned.Value.SmoothSpec) Then material.SmoothSpecTexture = cloned.Value.SmoothSpec

        If Logger.Enabled Then
            Dim hdptL = candidate.HeadPartHdptFormID
            Dim dL = cloned.Value.Diffuse, nL = cloned.Value.Normal, sL = cloned.Value.SmoothSpec
            Logger.LogLazy(Function() $"[HEADREAR-CLONE] ghoul-female nape hdpt=0x{hdptL:X8} → vanilla-UV clone D='{dL}' N='{nL}' S='{sL}'")
        End If
    End Sub

    ''' <summary>Resolve the VANILLA (BA2) bytes for a texture path and write them to a DISTINCT loose
    ''' path under <see cref="HeadRearClonedTextureRoot"/> (preserving the original sub-path so each
    ''' race's body texture clones to its own unique key), then register that loose entry so it resolves.
    ''' Idempotent: an existing clone of the same byte length is reused. Returns the cloned RELATIVE path,
    ''' or "" when the source path is empty / has no resolvable bytes / write fails.
    '''
    ''' The vanilla bytes are the BA2 content: GetOverriddenEntries(path) holds the loser(s) when a loose
    ''' file overrides a BA2 — pick the first IsLosseFile=False entry. If nothing was overridden, the live
    ''' GetBytes(path) already returns the BA2 content (no loose override present).</summary>
    Private Function CloneVanillaTextureToLoose(relativeTexturePath As String) As String
        If String.IsNullOrEmpty(relativeTexturePath) Then Return ""

        ' FilesDictionary texture keys carry the "Textures\" prefix (built from Data-relative paths),
        ' but material diffuse/normal/spec paths are stored WITHOUT it. Normalize exactly the way the
        ' app's live texture-load path does (Render.vb GetTextureID → CorrectTexturePath): corrects
        ' separators, strips any absolute prefix, and adds "Textures\" idempotently (case-insensitive).
        ' Without this the GetOverriddenEntries / GetBytes lookups miss → 0 bytes → "" → no override.
        Dim normalized = FO4UnifiedMaterial_Class.CorrectTexturePath(relativeTexturePath)
        If String.IsNullOrEmpty(normalized) Then Return ""
        ' Resolve vanilla (BA2) bytes directly from the archive, bypassing _bytesCache (which is keyed by
        ' FullPath and collides with the loose winner sharing the same path — see GetArchiveOriginalBytes).
        ' If no archived entry exists (no loose override present), the live GetBytes(normalized) winner is
        ' already the BA2 content.
        Dim vanillaBytes As Byte() = FilesDictionary_class.GetArchiveOriginalBytes(normalized)
        If vanillaBytes Is Nothing OrElse vanillaBytes.Length = 0 Then
            vanillaBytes = FilesDictionary_class.GetBytes(normalized)
        End If
        If vanillaBytes Is Nothing OrElse vanillaBytes.Length = 0 Then Return ""

        ' Distinct clone key = dedicated root + original sub-path (minus the Textures\ prefix). Encodes
        ' the race in the path itself (e.g. ...\Actors\Character\GhoulFemale\GhoulFemaleBody_d.dds), so
        ' no race-name literal is needed and different races never collide.
        Dim clonedRelPath = HeadRearClonedTextureRoot & normalized.StripPrefix(TexturesPrefix)
        Dim clonedFullPath = IO.Path.Combine(FilesDictionary_class.FO4Path, clonedRelPath)

        Try
            Dim needWrite As Boolean = True
            If IO.File.Exists(clonedFullPath) Then
                Dim existingLen As Long = New IO.FileInfo(clonedFullPath).Length
                If existingLen = vanillaBytes.LongLength Then needWrite = False
            End If
            If needWrite Then
                Dim dir = IO.Path.GetDirectoryName(clonedFullPath)
                If Not String.IsNullOrEmpty(dir) AndAlso Not IO.Directory.Exists(dir) Then IO.Directory.CreateDirectory(dir)
                IO.File.WriteAllBytes(clonedFullPath, vanillaBytes)
            End If
        Catch ex As Exception
            Dim srcL = normalized, msgL = ex.Message
            Logger.LogLazy(Function() $"[HEADREAR-CLONE] write FAILED src='{srcL}' → {msgL}")
            Return ""
        End Try

        ' Register the loose clone so GetBytes(clonedRelPath) reads Path.Combine(FO4Path, FullPath).
        FilesDictionary_class.AddOrUpdateDictionaryEntry(clonedRelPath,
            New FilesDictionary_class.File_Location With {.FullPath = clonedRelPath, .BA2File = ""})

        Return clonedRelPath
    End Function

    Friend Sub ApplyShapeMaterialOverrides(candidate As MainForm.MeshCandidate, state As MainForm.NPCVisualState, shapes As IEnumerable(Of IRenderableShape))
        If shapes Is Nothing Then Return

        Dim logEnabled = Logger.Enabled
        DumpAllTxstFlagsOnce()  ' diagnóstico one-shot: todos los TXST + flag (gateado por Logger.Enabled)

        If logEnabled Then
            Dim candFidLog As UInteger = If(candidate IsNot Nothing, candidate.SourceFormID, 0UI)
            Dim chunkOmodLog As UInteger = If(candidate IsNot Nothing, candidate.ChunkOmodFormID, 0UI)
            Dim candKindLog As String = If(candidate IsNot Nothing, candidate.Kind.ToString(), "<no-cand>")
            Dim ctxLog As String = If(candidate IsNot Nothing AndAlso candidate.OmodResolutionFormType IsNot Nothing, candidate.OmodResolutionFormType, "")
            Dim mswpLog As UInteger = If(candidate IsNot Nothing, candidate.MaterialSwapFormID, 0UI)
            Dim cremapLog As String = If(candidate IsNot Nothing AndAlso candidate.ColorRemapIndex.HasValue, candidate.ColorRemapIndex.Value.ToString("F4"), "none")
            Dim hasOmodResLog As Boolean = candidate IsNot Nothing AndAlso candidate.OmodResolution IsNot Nothing
            Dim shapeCountLog As Integer = shapes.Count()
            Logger.LogLazy(Function() $"[SHAPEMAT-ENTRY] cand=0x{candFidLog:X8} kind={candKindLog} chunkOmod=0x{chunkOmodLog:X8} ctxFormType='{ctxLog}' shapes={shapeCountLog} armaMSWP=0x{mswpLog:X8} armaColorRemap={cremapLog} hasOmodResolution={hasOmodResLog}")
        End If

        ' Material override pipeline order (matches engine application order):
        '   1. ARMA-direct base swap (MaterialSwapFormID + ColorRemapIndex per gender on the ARMA
        '      record itself — semantically SET).
        '   2. OBTS/OMOD resolution from the parent ARMO — DirectProperties of applied
        '      combinations, then Properties of every IncludedOmod, in declaration order.
        '      SET overwrites the current material; ADD muta lo que dejó la pasada anterior.
        ' (3) Texture/Skin/Hair palette overrides happen later in this method and read whatever
        ' material this pipeline left in place.
        If candidate IsNot Nothing AndAlso candidate.MaterialSwapFormID <> 0UI Then
            ' Draft-MSWP handling: a material-swap authored as an in-memory MSWP draft (provisional 0xFF FormID)
            ' has NO real record, so the lib's FormID overload (GetRecord+ParseMSWP) can't resolve it. Resolve it
            ' via the app's MswpDraftResolver instead — it returns the draft's ALREADY-PARSED MSWP_Data, which we
            ' hand to the parsed-data overload so an UNSAVED swap applies live in the preview. An unresolvable
            ' draft (resolver Nothing / not registered) falls back to the skip-with-log behavior. A swap that
            ' references an EXISTING (real) MSWP — even from a draft ARMA/ARMO — is a normal FormID and takes the
            ' unchanged FormID overload path below.
            If OutfitDraft.IsDraftFormID(candidate.MaterialSwapFormID) Then
                Dim d = _ctx.MswpDraftResolver?.Invoke(candidate.MaterialSwapFormID)
                If d IsNot Nothing Then
                    ShapeMaterialOverrides.ApplyMaterialSwap(d,
                                                            ShapeMaterialOverrides.MaterialSwapFunction.SET,
                                                            shapes)
                Else
                    Logger.LogLazy(Function() $"[DRAFT-MSWP] preview skips unresolvable material-swap draft 0x{candidate.MaterialSwapFormID:X8} (applies after Save)")
                End If
            Else
                ShapeMaterialOverrides.ApplyMaterialSwap(candidate.MaterialSwapFormID,
                                                        ShapeMaterialOverrides.MaterialSwapFunction.SET,
                                                        shapes, _ctx.PluginManager)
            End If
        End If
        If candidate IsNot Nothing AndAlso candidate.ColorRemapIndex.HasValue Then
            ShapeMaterialOverrides.ApplyColorRemap(candidate.ColorRemapIndex.Value, 0.0F,
                                                   ShapeMaterialOverrides.ColorRemapFunction.SET,
                                                   shapes)
        End If
        If candidate IsNot Nothing AndAlso candidate.OmodResolution IsNot Nothing Then
            ' FormType context comes from the candidate. Humanoid path (CollectArmoCandidates)
            ' sets "ARMO"; NPC robot path (CollectRobotChunkCandidates) sets "NPC_". Drives
            ' which PropertyIndex enum interprets each Property idx.
            OmodResolutionApplier.ApplyResolutionToShapes(candidate.OmodResolution, candidate.OmodResolutionFormType, shapes, _ctx.PluginManager)
        End If

        Dim solidTintColor = ResolveHeadPartSolidTintColor(candidate)
        Dim hairTintColor = ResolveHairTintColor(candidate, state, solidTintColor)
        Dim skinTintColor = ResolveSkinTintColor(candidate, state, solidTintColor)
        Dim textureSet = ResolveTextureSet(candidate, state)

        ' Skin substitution per-shape para Outfit: el engine vanilla sustituye la diffuse de shapes
        ' con shader SkinTint dentro de un outfit (escote, brazos expuestos) por la del actor's body
        ' skin (race-specific). Sólo aplica a Outfit. HeadParts usan TXST propio del HDPT (o FaceTint
        ' shader para Face). Skin candidates conservan TXST nativo via ARMA.
        Dim actorBodySkinTxst As TXST_Data = Nothing
        If candidate IsNot Nothing AndAlso candidate.Kind = MainForm.MeshCandidateKind.Outfit Then
            Dim region = ResolveSkinRegionForOutfit(candidate)
            actorBodySkinTxst = ResolveActorSkinTextureSet(state, region)
        End If

        For Each shape In shapes
            MaterialResolver.EnsureShapeMaterialResolved(shape)

            Dim relatedMaterial = shape.ShapeMaterial
            If relatedMaterial Is Nothing Then Continue For

            Dim matPre = relatedMaterial.material
            If logEnabled AndAlso matPre IsNot Nothing Then
                Dim palOnPre = matPre.GrayscaleToPaletteColor
                Dim palScalePre = matPre.GrayscaleToPaletteScale
                Dim greyTexPre = If(matPre.GreyscaleTexture, "")
                Dim shapeNamePre = shape.ShapeName
                Logger.LogLazy(Function() $"[PALSCALE-PRE] shape='{shapeNamePre}' path='{relatedMaterial.path}' palColor={palOnPre} palScale={palScalePre:F4} greyTex='{greyTexPre}' (post-load, pre-overrides)")

                ' Snapshot del material INLINE del NIF/BGSM ANTES de cualquier override TXST/FTST.
                ' Para ojos esto muestra la FUENTE de EyeGloss_n / eyeenvironmentmask_m (lo que el
                ' shader de ojos trae) vs lo que después intenta pisar el TXST (EyeBrown_n / Eye_s).
                Dim shP = matPre.NifShaderType.ToString()
                Dim isBgsmP = matPre.IsBGSM()
                Dim dP = If(matPre.Diffuse_or_Base_Texture, "")
                Dim nP = If(matPre.NormalTexture, "")
                Dim sP = If(matPre.SmoothSpecTexture, "")
                Dim specP = If(matPre.SpecularTexture, "")
                Dim wP = If(matPre.WrinklesTexture, "")
                Dim envP = If(matPre.EnvmapTexture, "")
                Logger.LogLazy(Function() $"[SHAPEMAT-PRE-TEX] shape='{shapeNamePre}' shader={shP} isBGSM={isBgsmP} (inline NIF/BGSM source, pre-TXST) D='{dP}' N='{nP}' S='{sP}' spec='{specP}' W='{wP}' env='{envP}'")
            End If

            ApplyTextureSetOverrides(textureSet, relatedMaterial, candidate.UsesBodyTexture, shape.NifShape, shape.NifContent,
                                     isHeadPartTextureSet:=(candidate IsNot Nothing AndAlso candidate.Kind = MainForm.MeshCandidateKind.HeadPart),
                                     isFaceHeadPart:=(candidate IsNot Nothing AndAlso candidate.HeadPartType = HeadPartTypeFace))

            Dim material = relatedMaterial.material
            If material Is Nothing Then Continue For

            If logEnabled Then
                Dim palOnPostTxst = material.GrayscaleToPaletteColor
                Dim palScalePostTxst = material.GrayscaleToPaletteScale
                Dim shapeNamePre = shape.ShapeName
                Logger.LogLazy(Function() $"[PALSCALE-POST-TXST] shape='{shapeNamePre}' palColor={palOnPostTxst} palScale={palScalePostTxst:F4} (post TXST/MNAM override)")
            End If

            ' Shape con piel expuesta (shader=SkinTint): sustituir SÓLO sus texturas (diffuse +
            ' normal + spec) por las del body skin del actor (race-specific). Material params
            ' (specular, smoothness, subsurface, etc.) NO se tocan — vienen del NIF original.
            ' Decisión per-shape via material.NifShaderType porque un mismo .nif suele tener shapes
            ' mixtos. El render lee el path desde relatedMaterial.material (Render.vb:1362).
            If actorBodySkinTxst IsNot Nothing AndAlso material.NifShaderType = NiflySharp.Enums.BSLightingShaderType.SkinTint Then
                Dim diffuseBefore = material.Diffuse_or_Base_Texture
                ' Si el TXST trae MaterialPath (MNAM .bgsm), las texturas viven dentro del BGSM —
                ' cargar el BGSM para extraer sus paths. NO copiamos otros params del BGSM (sólo
                ' las texturas), preservando los params del material original del shape.
                If actorBodySkinTxst.MaterialPath <> "" Then
                    Dim bgsmMaterial = MaterialResolver.TryLoadMaterialFromDictionary(actorBodySkinTxst.MaterialPath, material, shape.NifShape, shape.NifContent)
                    If bgsmMaterial IsNot Nothing Then
                        If bgsmMaterial.Diffuse_or_Base_Texture <> "" Then material.Diffuse_or_Base_Texture = bgsmMaterial.Diffuse_or_Base_Texture
                        If bgsmMaterial.NormalTexture <> "" Then material.NormalTexture = bgsmMaterial.NormalTexture
                        If bgsmMaterial.SmoothSpecTexture <> "" Then material.SmoothSpecTexture = bgsmMaterial.SmoothSpecTexture
                        If logEnabled Then
                            Dim mnamL = If(actorBodySkinTxst.MaterialPath, "")
                            Dim shapeL = shape.ShapeName
                            Logger.LogLazy(Function() $"[SKINSUB-MNAM] shape='{shapeL}' bodyBgsm='{mnamL}' → copia D/N/SmoothSpec del BGSM body (otros params del NIF; SKIP)")
                        End If
                    End If
                End If
                If logEnabled Then
                    Dim shapeSubL = shape.ShapeName
                    Logger.LogLazy(Function() $"[SKINSUB] shape='{shapeSubL}' SkinTint en Outfit → sustituye texturas por body skin del actor (luego TXST slots encima)")
                End If
                ApplyTextureSetToMaterial(material, actorBodySkinTxst)
            End If

            ' Hair/Palette + HairTintColor: shared with RefreshFaceTintLivePreview via helper.
            ' Pre-resolved hairTintColor (incl. solidTintColor head-part color) passed as override
            ' so the helper can short-circuit ResolveColorFormColor for hair HeadParts whose
            ' candidate carries a richer color choice. Helper is the single source of truth for
            ' the engine-faithful gate (Hair/FacialHair/Brow HDPTs only) — removes the prior
            ' If/ElseIf duplication and the looser parallel copy in RefreshFaceTintLivePreview.
            ApplyMaterialPaletteHairColor(material, candidate, state, hairTintColor)

            ' Skin-tint FIEL al material resuelto (SIN force). El render tinta los shapes cuyo material
            ' resolvió SkinTint=True — piel real (body/hands/rear-head) ya viene SkinTint de su fuente
            ' (verificado en log: preST=True en todos esos). Se ELIMINÓ ShouldForceSkinTint: era
            ' redundante para piel real y forzaba MAL a no-piel (PAFrame01/Stingwing/basesuit por el
            ' catch-all Kind=Skin). MouthShadow/bocas humanas/ojos/lashes nunca lo necesitaron (force=False
            ' en el log). Ahora el material resuelto manda y nada se muta para el render.
            If logEnabled Then
                Dim shapeNameST = shape.ShapeName
                Dim matShaderST = material.NifShaderType.ToString()
                Dim stVal = material.SkinTint.ToString()
                Logger.LogLazy(Function() $"[SKINTINT-RESOLVED] shape='{shapeNameST}' matShader={matShaderST} SkinTint={stVal} (faithful, no force)")
            End If

            If material.SkinTint AndAlso skinTintColor.HasValue Then
                material.SkinTintColor = skinTintColor.Value
            End If

            If solidTintColor.HasValue AndAlso Not material.Hair AndAlso Not material.SkinTint Then
                shape.TintColor = solidTintColor.Value
            End If

            ' Ghoul-female head-rear: overwrite D/N/S with the vanilla-UV body texture CLONE (distinct
            ' path key holding the BA2 vanilla bytes). Runs last so it wins over whatever the embedded
            ' material / UsesBodyTexture path resolved. No-op for every other shape. Shared by the full
            ' render, the fast-path skin refresh, and the FaceGen bake (delegate into this method).
            ApplyGhoulHeadRearClonedTextures(candidate, state, material, shape)

            If logEnabled Then
                Dim shapeNameFinal = shape.ShapeName
                Dim pathFinal = If(relatedMaterial.path, "")
                Dim rootFinal = If(material.RootMaterialPath, "")
                Dim shaderFinal = material.NifShaderType.ToString()
                Dim isBgsmFinal = material.IsBGSM()
                Dim palOnFinal = material.GrayscaleToPaletteColor
                Dim palScaleFinal = material.GrayscaleToPaletteScale
                Dim texDiff = If(material.Diffuse_or_Base_Texture, "")
                Dim texNorm = If(material.NormalTexture, "")
                Dim texGlow = If(material.GlowTexture, "")
                Dim texGrey = If(material.GreyscaleTexture, "")
                Dim texSpec = If(material.SpecularTexture, "")
                Dim texSmSpec = If(material.SmoothSpecTexture, "")
                Dim texEnv = If(material.EnvmapTexture, "")
                Dim texEnvMask = If(material.EnvmapMaskTexture, "")
                Dim texLight = If(material.LightingTexture, "")
                Dim texWrink = If(material.WrinklesTexture, "")
                Dim texInner = If(material.InnerLayerTexture, "")
                Dim texTintMask = If(material.TintMaskTexture, "")
                Logger.LogLazy(Function() $"[SHAPEMAT-FINAL] shape='{shapeNameFinal}' path='{pathFinal}' root='{rootFinal}' shader={shaderFinal} isBGSM={isBgsmFinal} palette={palOnFinal} palScale={palScaleFinal:F4}")
                Logger.LogLazy(Function() $"[SHAPEMAT-FINAL-TEX] shape='{shapeNameFinal}' diff='{texDiff}' norm='{texNorm}' glow='{texGlow}' grey='{texGrey}' spec='{texSpec}' smSpec='{texSmSpec}' env='{texEnv}' envMask='{texEnvMask}' light='{texLight}' wrink='{texWrink}' inner='{texInner}' tintMask='{texTintMask}'")
            End If
        Next
    End Sub

End Class
