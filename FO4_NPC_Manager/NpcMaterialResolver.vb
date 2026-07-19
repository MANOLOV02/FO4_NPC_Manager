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
    ''' <summary>Shared preset overlays (keyed by root NPC FormID) — the source of SSE RaceMenu skin overrides, which
    ''' are applied as an in-place texture-set slot replacement on the skin shape's material (see
    ''' <see cref="ApplySseSkinOverrideToMaterial"/>). Skyrim only; Nothing/absent on FO4.</summary>
    Private ReadOnly _appliedPresets As Dictionary(Of UInteger, LooksmenuLoader.LooksmenuPreset)
    Public Sub New(ctx As NpcRenderContext, overlayResolver As Func(Of NPC_Data, UInteger, NPC_Data),
                   Optional appliedPresets As Dictionary(Of UInteger, LooksmenuLoader.LooksmenuPreset) = Nothing)
        _ctx = ctx
        _overlayResolver = overlayResolver
        _appliedPresets = appliedPresets
    End Sub

    ''' <summary>Apply a RaceMenu skin override IN PLACE onto a skin shape's material — faithful to skee's
    ''' NIOVTaskUpdateTexture + GetTextureFromIndex (ShaderUtilities.cpp:161-180, 388-480). Skin overrides only
    ''' target <c>kShaderType_FaceGenRGBTint</c> geometries (OverrideInterface.cpp:1096), and for that material
    ''' <c>GetTextureFromIndex</c> maps ONLY four slots — 0→texture1 (diffuse), 1→texture2 (normal), 2→texture3
    ''' (subsurface _sk), 7→texture4 (specular/backlight); indices 3,4,5,6 return nullptr, i.e. do nothing. So we
    ''' replace exactly those four (present-only, keeping the skin's own texture in the rest), then key 7 tint
    ''' (NiColor, RGB only) → <c>SkinTintColor</c> and key 8 alpha → material <c>Alpha</c>. (Slots 3-6 are preserved
    ''' in the model for round-trip but are engine no-ops on a skin, so they are not applied.)</summary>
    Private Shared Sub ApplySseSkinOverrideToMaterial(material As FO4UnifiedMaterial_Class, sk As RaceMenuJslot.JslotSkinOverride)
        If material Is Nothing OrElse sk Is Nothing Then Return
        If sk.Slots IsNot Nothing Then
            For Each kvp In sk.Slots
                Dim path = kvp.Value
                If String.IsNullOrEmpty(path) Then Continue For   ' present-only: an empty/absent slot keeps the skin's own
                Select Case kvp.Key
                    Case 0 : material.Diffuse_or_Base_Texture = path   ' skee texture1 = texture-set slot 0
                    Case 1 : material.NormalTexture = path             ' skee texture2 = texture-set slot 1
                    Case 2
                        ' skee texture3 = texture-set slot 2. Route it EXACTLY as the engine-faithful reader
                        ' FO4UnifiedMaterial_Class.ReadBgsmTexturesFromTextureSet: for a skin material (Facegen /
                        ' SubsurfaceLighting / RimLighting, not Glowmap) slot 2 is the _sk subsurface on
                        ' LightingTexture; otherwise it is the glow map. (We gate to skin shapes, so normally the
                        ' subsurface.)
                        If Not material.Glowmap AndAlso (material.SubsurfaceLighting OrElse material.RimLighting OrElse material.Facegen) Then
                            material.LightingTexture = path
                        Else
                            material.GlowTexture = path
                        End If
                    Case 7 : material.SmoothSpecTexture = path         ' skee texture4 = texture-set slot 7 (specular/backlight)
                    ' 3,4,5,6: GetTextureFromIndex returns nullptr for a FaceGenRGBTint skin → engine no-op; skip.
                End Select
            Next
        End If
        If sk.HasTint Then
            material.SkinTintColor = Color.FromArgb(255, ClampUnitByte(sk.TintR), ClampUnitByte(sk.TintG), ClampUnitByte(sk.TintB))
            ' Mark it so the QNAM skin-tone pass (NpcFaceTintResolver) doesn't overwrite this explicit override tint;
            ' skee replays the override over the base tone, so the override wins.
            material.SkinTintFromOverride = True
        End If
        If sk.HasAlpha Then material.Alpha = Math.Max(0.0F, Math.Min(1.0F, sk.Alpha))
    End Sub

    Private Shared Function ClampUnitByte(v As Single) As Integer
        Return Math.Max(0, Math.Min(255, CInt(Math.Round(v * 255.0F))))
    End Function

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
            ' ⭐ HDPT.CNAM (Head Part Color) GANA sobre NPC.HCLF, por head part. Se resuelve ACÁ —punto
            ' compartido render+bake— porque el camino del RENDER llama con hairTintColorOverride:=Nothing
            ' (NpcFaceTintResolver.vb:1184) y sin esto vería el HCLF mientras el bake ve el CNAM: RENDER ≠ BAKE.
            ' MEDIDO: sólo 5 HDPT en todo vanilla+DLC tienen CNAM<>0 (pelo y hairline de Serana y Valerica,
            ' todas → 0x000A0434 HairColor11Black). CK hornea (52,56,56) = 2×(26,28,28) del CNAM; nosotros
            ' dábamos el HCLF del NPC. Sus cejas (sin CNAM) coinciden en ambos lados y caen al HCLF, lo que
            ' confirma que la precedencia es por head part y que el ×2 estaba bien.
            ' ⛔ Gate = `CNAM <> 0`, NO el flag DATA 0x10 "Use Solid Tint" (ninguna de las 5 lo tiene y el CK
            ' usó el CNAM igual).
            If Not effectiveHairColor.HasValue Then
                effectiveHairColor = ResolveHeadPartSolidTintColor(candidate)
            End If
            If Not effectiveHairColor.HasValue AndAlso hairColorFormID <> 0UI Then
                effectiveHairColor = ResolveColorFormColor(hairColorFormID)
            End If
            If effectiveHairColor.HasValue Then
                Dim oldHairCol = material.HairTintColor
                Dim resolvedHair = effectiveHairColor.Value
                ' ⭐ SSE hair-tint convention: el FaceGeom (y el engine) usan el color del CLFM DOBLADO
                ' (clamp 255). Medido byte-exacto vs CK sobre el corpus vanilla: CK.HairTintColor == 2×CLFM.Color
                ' (p.ej. Narri CLFM=(48,35,33) → CK=(96,70,66)). Sin el ×2 el render Y el bake muestran el pelo
                ' a la mitad (más apagado). Es un ÚNICO punto de resolución que consumen render y bake, así que
                ' arregla los dos a la vez. FO4 usa el path de grayscale-palette (rama HasRemappingIndex de
                ' arriba), NO este HairTintColor, así que queda intacto (validado byte-exact).
                '
                ' ⚠️ El ×2 va por TintColorScale (dominio FLOAT), NO doblando los bytes. El storage del
                ' material son 3 BYTES (0x00RRGGBB) ⇒ techo duro 255 = 1.0: con un CLFM de canal ≥128 el
                ' doblado en bytes clampeaba y perdía el exceso. MEDIDO vs CK: CLFM=(130,130,130) →
                ' CK=(1,020,1,020,1,020) = 2,0 × (130/255), nuestro doblado en bytes daba min(255,260)/255
                ' = (1,000,1,000,1,000) — Δ=0,0196 en 9 NPCs / 25 shapes (p.ej. BrowsMaleSnowElf). El
                ' factor se aplica al convertir a float en el bake (Save_To_Shader) y en el render
                ' (mismo valor, RENDER == BAKE); el shader tolera tint > 1.
                ' Se escribe SIEMPRE (1.0F fuera de SSE) para no arrastrar estado si el material se reutiliza.
                Dim isSseHairDouble As Boolean = Config_App.Current IsNot Nothing AndAlso Config_App.Current.Game = Config_App.Game_Enum.Skyrim
                material.TintColorScale = If(isSseHairDouble, 2.0F, 1.0F)
                material.HairTintColor = resolvedHair
                If logEnabled Then
                    Dim newColLog = resolvedHair
                    Dim scaleLog = material.TintColorScale
                    Logger.LogLazy(Function() $"[HAIRTINT-WRITE] hdptType={candidate.HeadPartType} oldRGB=({oldHairCol.R},{oldHairCol.G},{oldHairCol.B}) → newRGB=({newColLog.R},{newColLog.G},{newColLog.B}) scale={scaleLog:F2} effective=({newColLog.R / 255.0F * scaleLog:F3},{newColLog.G / 255.0F * scaleLog:F3},{newColLog.B / 255.0F * scaleLog:F3})")
                End If
            ElseIf hairColorFormID = 0UI AndAlso Config_App.Current IsNot Nothing AndAlso Config_App.Current.Game = Config_App.Game_Enum.Skyrim Then
                ' NPC without a hair color (HCLF absent) — CK writes HairTintColor=(0,0,0) instead of keeping the
                ' source hair mesh's tint. MEDIDO vs BSA CK: the 3 vanilla Dremora with HCLF=None bake (0,0,0),
                ' while the source hairline mesh carries a non-zero tint; without this the baked hair keeps that
                ' stray source color. SSE-only, hair/facialhair/brow-only (IsHairHeadPart gate above).
                material.TintColorScale = 1.0F   ' negro: sin doblado (y no arrastrar scale de un uso previo)
                material.HairTintColor = Color.FromArgb(material.HairTintColor.A, 0, 0, 0)
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

        ' Máscaras de slot GAME-AWARE (FO4 vs SSE difieren: FO4 Body=slot33/bit3, Hands=slots34-35;
        ' SSE Body=slot32/bit2, Hands=slot33/bit3). Fuente única = BipedSlots._fo4Regions/_sseRegions.
        ' Sin esto, en SSE el bit3 (=Hands) matcheaba el viejo BODY_BIT → la ARMA de MANOS se elegía
        ' para la región Body y el cuerpo del outfit recibía la textura de manos.
        Dim bodyMask As UInteger = BipedSlots.RegionMask(BipedSlots.BipedRegion.Body)
        Dim handMask As UInteger = BipedSlots.RegionMask(BipedSlots.BipedRegion.Hands)

        ' ⭐ LA RAZA ES UNA *PREFERENCIA*, NO UN GATE (bake controlado BAKETEST2FO4.esp, 2026-07-19).
        '
        ' MEDIDO — el CK NO filtra por raza las ARMA del skin ARMO. Los NPC BAKETEST2_N_D1 (0x840,
        ' brazo NPC.WNAM) y BAKETEST2_R_D1 (0x847, brazo RACE.WNAM) tienen un skin ARMO cuya ÚNICA
        ' ARMA de slot body declara RNAM=SynthGen2Race (sin additional races): no matchea ni la raza
        ' del actor (HumanRace / clon de HumanRace) ni la RNAM del ARMO. El CK igual le horneó al
        ' head-rear las texturas de ESA ARMA (Actors\Character\Piper\PiperHead_d/_n/_s). Con el gate
        ' estricto anterior el resolver devolvía Nothing y el shape se quedaba con las texturas
        ' embebidas del NIF fuente (FemaleBody_*), que NO aparecen en ninguno de los 9 NIF horneados.
        ' Controles positivos del experimento: N_P→Preston_*, R_P→Mayor_* (los dos PASARON, así que
        ' los dos brazos WNAM miden). El ARMO viene del WNAM ⇒ es la piel del actor POR CONSTRUCCIÓN,
        ' y por eso el CK no necesita re-validarle la raza.
        '
        ' PERO el gate NO se puede simplemente borrar: sobre el corpus vanilla hay 120 NPC FO4 + 3 SSE
        ' cuyo skin ARMO lista VARIAS ARMA de slot body de razas distintas, y ahí la primera de la
        ' lista es la equivocada — YaoGuai tomaría FEVHoundWholeAA, EyeBot tomaría BloatflyWholeAA,
        ' Netch (SSE) tomaría DLC2NakedRieklingAA. El experimento no discrimina ese caso porque sus
        ' ARMO tienen UNA sola ARMA de body.
        '
        ' FORMULACIÓN que satisface las dos evidencias (y la única mínima que lo hace): dos pasadas.
        '   Pasada 1 = comportamiento anterior EXACTO (sólo ARMA race-válidas).
        '   Pasada 2 = sólo si la pasada 1 no resolvió nada, se acepta cualquier ARMA de la región.
        ' Es estrictamente ADITIVA: nunca cambia una elección que ya resolvía, sólo convierte
        ' "Nothing" en "la ARMA que el CK sí usa". Por eso los 120+3 quedan bit-idénticos y los 212
        ' NPC FO4 de ARMA única non-matching (la clase SkinSynthGen2/SynthGen2Body, que incluye al
        ' DN092_IntercomFemale01 del RE original) pasan a resolver como el CK.
        ' ⚠️ SUBDETERMINADO: el desempate cuando hay VARIAS ARMA non-matching de la región (la
        ' pasada 2 toma la primera). El experimento no lo mide; no hay caso vanilla que lo separe.
        For pass As Integer = 0 To 1
            Dim requireRaceMatch As Boolean = (pass = 0)
            For Each entry In armo.ArmorAddons
                Dim arma = _ctx.GetParsedArma(entry.ArmaFormID)
                If arma Is Nothing Then Continue For
                If requireRaceMatch AndAlso
                   Not MainForm.ArmorAddonMatchesRace(arma, state.RaceFormID, _ctx.GetEffectiveArmorRaces(state.RaceFormID)) Then Continue For
                Dim armaSlot = arma.SlotMask

                Dim matches As Boolean = False
                Select Case region
                    Case MainForm.SkinRegion.Body
                        matches = (armaSlot And bodyMask) <> 0UI
                    Case MainForm.SkinRegion.Hand
                        matches = (armaSlot And handMask) <> 0UI AndAlso (armaSlot And bodyMask) = 0UI
                End Select
                If Not matches Then Continue For

                ' Fallback EXACTO del motor (getter 0x140a90790: [arma+sex*8+0x240], null→índice0=NAM0/male):
                ' female → NAM1, si vacío → NAM0 (male). male → NAM0 (sin fallback a female).
                ' Confirmado por el bake: BAKETEST2_N_G (0x846) tiene ARMA_G con SÓLO NAM0 y NPC femenino
                ' → el CK horneó OldHumanMaleHead_* (o sea female cae a NAM0).
                Dim txstFID = If(state.IsFemale,
                                 If(arma.FemaleSkinTextureFormID <> 0UI, arma.FemaleSkinTextureFormID, arma.MaleSkinTextureFormID),
                                 arma.MaleSkinTextureFormID)
                If txstFID = 0UI Then Continue For

                Dim txstRec = _ctx.PluginManager.GetRecord(txstFID)
                If txstRec Is Nothing OrElse txstRec.Header.Signature <> "TXST" Then Continue For

                If Logger.Enabled AndAlso Not requireRaceMatch Then
                    Dim aEid = arma.EditorID, rFid = state.RaceFormID
                    Logger.LogLazy(Function() $"[SKINTXST-RACEFALLBACK] ninguna ARMA race-válida cubrió la región; se acepta '{aEid}' (race del actor 0x{rFid:X8}) — regla BAKETEST2 N_D1/R_D1")
                End If
                Return RecordParsers.ParseTXST(txstRec, _ctx.PluginManager)
            Next
        Next

        Return Nothing
    End Function

    ''' <summary>Decide qué región de skin (Body vs Hand) corresponde a un Outfit candidate según
    ''' su SlotMask. Outfits tipo "MOutfit/FOutfit" (cubren BODY+[U]) → Body; gloves outfits (sólo
    ''' bits hand sin BODY/[U]) → Hand. Para [A] over-armor con piel expuesta (raro), el slot
    ''' indica qué cubre — si toca BODY/[U] usar Body; si sólo [A]/hand → Hand.</summary>
    Friend Shared Function ResolveSkinRegionForOutfit(candidate As MainForm.MeshCandidate) As MainForm.SkinRegion
        If candidate Is Nothing Then Return MainForm.SkinRegion.Body
        ' Máscaras GAME-AWARE (BipedSlots._fo4Regions/_sseRegions). FO4 Body=slot33/bit3 + [U] slots36-40;
        ' SSE Body=slot32/bit2 (+feet/calves), Hands=slot33/bit3, sin [U]. Antes bits FO4 fijos → en SSE
        ' un outfit de cuerpo (slot32/bit2) no matcheaba Body y bit3 (=Hands SSE) se leía como Body.
        Dim bodyMask As UInteger = BipedSlots.RegionMask(BipedSlots.BipedRegion.Body)
        Dim handMask As UInteger = BipedSlots.RegionMask(BipedSlots.BipedRegion.Hands)
        Dim underMask As UInteger = BipedSlots.RegionMask(BipedSlots.BipedRegion.Under)

        Dim slot = candidate.SlotMask
        Dim touchesBodyOrU = (slot And bodyMask) <> 0UI OrElse (slot And underMask) <> 0UI
        Dim touchesHand = (slot And handMask) <> 0UI

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

    ''' <param name="isFaceTextureSource">Solo FO4: True cuando el TXST retornado es el NPC.FTST del
    ''' camino Face. El caller lo aplica DIFFUSE-ONLY (forceDiffuseOnly) — el attach del engine
    ''' (Fallout4.exe 0x1406EE0B2 / CK 0x140ED3807) aplica de la cadena FTST>bodyTex>TNAM únicamente
    ''' el slot 0. En SSE es SIEMPRE False (base=TNAM; la capa face va en
    ''' <paramref name="sseFaceAuxTextureSet"/>). RE completo 4 binarios 2026-07-16, ver memoria
    ''' arch_engine_face_texture_pipeline_re.</param>
    ''' <param name="sseFaceAuxTextureSet">SOLO SSE + HeadPart raw=Face: el set RESUELTO
    ''' FTST ?? DFT[sexo propio] ?? TNAM que aporta ÚNICAMENTE Normal(TX01) + _sk(TX03) +
    ''' detail(TX04) POR ENCIMA de la base TNAM, gateado por material Face — modelo por capas del
    ''' motor SSE medido 2026-07-16 con TRES evidencias: (1) RE RegenerateHead 0x14042BD90 (normal
    ''' vía 0x14042C410→material+0x58; _sk/detail→material+0xB0/+0xA8; TX07 solo togglea flag
    ''' specular, no path; el DIFFUSE jamás se toca — queda del TNAM del attach); (2) facegeom
    ''' SHIPPED de Razhinda 0x0001B1D3 (FTST=SkinHeadMaleKhajiitOld): D y S del TNAM femenino,
    ''' N/_sk/detail del FTST masculino — cada slot discrimina; (3) facegeom SHIPPED del Afflicted
    ''' 0x00064A42: N=BretonMale_msn del DFT de la raza pisando el TNAM (prueba DFT>TNAM en la capa).
    ''' Nothing en FO4 (allá el set resuelto reemplaza D/N/S completos como base del composite
    ''' FaceCustomization — validado byte-exacto vs CK, Mitch) o si el aux coincide con el TNAM.</param>
    ''' <param name="fo4FaceComposeInputsOnly">SOLO FO4 + HeadPart raw=Face, y SOLO cuando el set
    ''' resuelto vino de la CADENA FTST (NPC.FTST o RACE.DFTM-fallback), no del TNAM del head part.
    ''' True ⇒ el caller aplica del TXST ÚNICAMENTE TX00/TX01/TX07 (D/N/S = las tres ENTRADAS del
    ''' compose de FaceCustomization). Ver la regla medida en <see cref="ApplyTextureSetToMaterial"/>.</param>
    Friend Function ResolveTextureSet(candidate As MainForm.MeshCandidate, state As MainForm.NPCVisualState, ByRef isFaceTextureSource As Boolean, ByRef sseFaceAuxTextureSet As TXST_Data, ByRef fo4FaceComposeInputsOnly As Boolean) As TXST_Data
        isFaceTextureSource = False
        sseFaceAuxTextureSet = Nothing
        fo4FaceComposeInputsOnly = False
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
            ' Resolución Face por JUEGO (dos leyes distintas, ambas medidas — ver doc del parámetro
            ' sseFaceAuxTextureSet):
            '   FO4 — el set resuelto (FTST > TNAM > DFTM-si-no-hay-TNAM) reemplaza la BASE COMPLETA
            '   (D/N/S) del composite FaceCustomization; ej. Mitch FTST=SkinHeadMayor pisa
            '   MaleHeadHuman.TNAM=SkinHeadHeroMale — validado byte-exacto vs bakes reales del CK.
            '   (El RE del resolver del CK 0x140ED4244 sugiere DFT>TNAM también con TNAM presente;
            '   NO aplicado: cero casos vanilla para validar contra bake — pendiente experimento CK
            '   con esp de prueba antes de cambiar la precedencia FO4.)
            '   SSE — MODELO POR CAPAS: base = TNAM (D y S SIEMPRE del TNAM; el motor jamás pisa el
            '   diffuse con FTST/DFT); capa aux = FTST ?? DFT[sexo propio] ?? TNAM que aporta SOLO
            '   N/_sk/detail, aplicada gateada por material Face en el loop per-shape.
            ' Guard raw=Face (HeadPartTypeRaw, NO effective) protege sub-parts Misc heredados como
            ' Face (MouthShadow/AO/lashes/wet) que conservan su propio material (verificado Alijo).
            If candidate.Kind = MainForm.MeshCandidateKind.HeadPart AndAlso candidate.HeadPartTypeRaw = HeadPartTypeFace AndAlso state IsNot Nothing Then
                Dim isSse As Boolean = (Config_App.Current IsNot Nothing AndAlso Config_App.Current.Game = Config_App.Game_Enum.Skyrim)
                If isSse Then
                    ' SSE — MODELO POR SLOTS validado byte contra 2770/2770 facegeom shipped vanilla+DLC
                    ' (RE RegenerateHead 0x14042BD90):
                    '   D(material+0x48)  = TNAM del head part (attach 0x14042BAA0, slot0 → SOLO diffuse)
                    '   N(material+0x58)  = resolved.TX01  (regen 0x14042C410)
                    '   _sk(material+0xb0)= resolved.TX03  (regen 0x14042C0DD)  [→ LightingTexture, remap SSE]
                    '   detail            = resolved.TX04  (regen 0x14042C173)  [→ DisplacementTexture]
                    '   S(TX07) y resto   = AUTORADO del NIF (NADIE lo escribe: attach solo D, regen solo
                    '                       N/_sk/detail; TX07 del regen solo togglea un flag, no el path).
                    ' resolved = FTST > DFT[sexo propio]. SIN fallback a TNAM: si no hay FTST ni DFT el
                    ' resolved NO existe ⇒ la capa aux no corre ⇒ N/_sk/detail quedan AUTORADOS (caso
                    ' Astrid: NordRaceAstrid sin FTST/DFTF → S=femalehead_s autorado, no AstridHead_s).
                    ' Por eso la BASE TNAM va DIFFUSE-ONLY (isFaceTextureSource=True): pisa SOLO el D; N/_sk
                    ' los pisa la capa aux cuando hay resolved, y S/detail quedan del material autorado.
                    ' resolved = FTST > DFT[sexo] > TNAM (RE arch_engine_face_texture_pipeline_re). La capa aux
                    ' aporta N/_sk/detail SOLO cuando el resolved DIFIERE del TNAM (hay FTST o DFT). Si NO hay
                    ' FTST ni DFT, resolved = TNAM ⇒ el TNAM se aplica COMPLETO (incluye TX03=_sk), NO diffuse-only.
                    ' Ej. DATA-DRIVEN: KhajiitRace.DFTM=0 (sin DFT) + EnhancedKhajiit override del TNAM
                    ' SkinHeadMaleKhajiit con TX03=khajiitmalehead_sk → resolved=TNAM → _sk=khajiitmalehead_sk (=CK).
                    Dim auxFid As UInteger = 0UI
                    Dim auxSource As String = ""
                    If state.ExplicitHeadTextureFormID <> 0UI Then
                        auxFid = state.ExplicitHeadTextureFormID : auxSource = "NPC.FTST(Face-aux)"
                    ElseIf state.HeadTextureFormID <> 0UI Then
                        auxFid = state.HeadTextureFormID : auxSource = "RACE.DFTM(Face-aux)"
                    End If
                    If auxFid <> 0UI AndAlso auxFid <> textureSetFormID Then
                        Dim auxRec = _ctx.PluginManager.GetRecord(auxFid)
                        If auxRec IsNot Nothing AndAlso auxRec.Header.Signature = "TXST" Then
                            sseFaceAuxTextureSet = RecordParsers.ParseTXST(auxRec, _ctx.PluginManager)
                            If logEnabled Then
                                Dim aSrc = auxSource, aP = sseFaceAuxTextureSet
                                Logger.LogLazy(Function() $"[TXST-RESOLVE] source={aSrc} txst=0x{aP.FormID:X8} eid='{If(aP.EditorID, "")}' → capa SSE N/_sk/detail (base TNAM=diffuse-only) N='{If(aP.NormalTexture, "")}' sk='{If(aP.GlowTexture, "")}' det='{If(aP.HeightTexture, "")}'")
                            End If
                        ElseIf logEnabled Then
                            Dim aFidL = auxFid, aSrc2 = auxSource
                            Logger.LogLazy(Function() $"[TXST-RESOLVE] source={aSrc2} formID=0x{aFidL:X8} → NOT-FOUND-or-not-TXST (aux SSE descartado)")
                        End If
                    End If
                    ' Base TNAM = DIFFUSE-ONLY SOLO cuando hay aux (resolved ≠ TNAM ⇒ N/_sk/detail los pone la
                    ' capa aux). SIN aux, resolved = TNAM ⇒ el TNAM va COMPLETO (N/_sk/detail/S incluidos, TX03=_sk).
                    ' Antes era True SIEMPRE ⇒ descartaba el TX03 del TNAM aunque no hubiera aux (bug del _sk del mod).
                    isFaceTextureSource = (sseFaceAuxTextureSet IsNot Nothing)
                Else
                    ' FO4 — precedencia FTST > TNAM > DFTM(si TNAM=0). El SET RESUELTO se aplica, pero
                    ' cuando viene de la cadena FTST se aplica SOLO TX00/TX01/TX07 (ver la regla medida
                    ' en ApplyTextureSetToMaterial, parámetro fo4FaceComposeInputsOnly).
                    ' ⛔ HISTORIA — por qué esto NO es la reversión de nuevo: en 2026-07-17 se revirtió el
                    ' cambio "FTST diffuse-only + DFTM fuera" porque el efecto neto del FTST sobre el
                    ' composite FaceCustomization NO estaba cerrado en el RE (874 NPCs vanilla en juego).
                    ' Ese hueco lo CERRÓ el bake controlado BAKETESTFO4.esp (47 NIFs + 282 DDS del CK,
                    ' 2026-07-18) y de paso REFUTÓ las dos hipótesis previas: "slot 0 únicamente" rompería
                    ' el _msn y el _s de esos 874 (TX01 y TX07 SÍ alimentan el compose), y "los 8 slots"
                    ' mete 5 slots de ruido en el NIF. La regla medida es la intermedia: TX00/TX01/TX07 al
                    ' compose, TX02-TX06 INERTES. Por eso isFaceTextureSource (=forceDiffuseOnly, slot 0
                    ' solo) sigue False acá: sería exactamente la hipótesis refutada.
                    If state.ExplicitHeadTextureFormID <> 0UI Then
                        textureSetFormID = state.ExplicitHeadTextureFormID
                        txstSource = "NPC.FTST(Face-override)"
                        fo4FaceComposeInputsOnly = True
                    ElseIf textureSetFormID = 0UI AndAlso state.HeadTextureFormID <> 0UI Then
                        textureSetFormID = state.HeadTextureFormID
                        txstSource = "RACE.DFTM(Face-fallback)"
                        fo4FaceComposeInputsOnly = True
                    End If
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
    Friend Shared Sub ApplyTextureSetOverrides(textureSet As TXST_Data, relatedMaterial As Nifcontent_Class_Manolo.RelatedMaterial_Class, usesBodyTexture As Boolean, shap As NiflySharp.INiShape, nif As Nifcontent_Class_Manolo, Optional isHeadPartTextureSet As Boolean = False, Optional isFaceHeadPart As Boolean = False, Optional forceDiffuseOnly As Boolean = False, Optional isNpcExplicitFaceTextureSet As Boolean = False, Optional fo4FaceComposeInputsOnly As Boolean = False)
        If textureSet Is Nothing OrElse relatedMaterial Is Nothing Then Return

        Dim logEnabled = Logger.Enabled
        Dim material = relatedMaterial.material
        If material Is Nothing Then Return

        ' MNAM-loaded rule (split by HDPT.UsesBodyTexture, verified empirically vs CK bake):
        '   - UsesBodyTexture=True : D + N + S from the override (and NOTHING else — corregido
        '     2026-07-19 por BAKETEST2 N_S/N_S2; antes decía "+ everything else", ver la nota en el
        '     cuerpo). La evidencia Alice ChildHeadRear (MNAM=childfemalebody.bgsm) NO discriminaba:
        '     ese BGSM declara SÓLO D/N/S, todos los demás slots vacíos.
        '   - UsesBodyTexture=False: diffuse-only. The MNAM just supplies the surface tint
        '     for this specific shape; Normal/SmoothSpec/Envmap/shaderType/EnvironmentMapping/
        '     TwoSided all stay from the inline NIF shader. Verified vs Valentine
        '     SynthGen2HeadRearValentine (TXST.MNAM=gen2skindirty.bgsm has type=Default
        '     no-Envmap, but CK bake kept inline type=EnvironmentMap with the Envmap path
        '     and the non-dirty SmoothSpec).
        ' ⛔ DEROGADO 2026-07-19 (BAKETEST2FO4.esp): decía "los TX## del TXST se aplican por encima,
        ' así que cualquier slot que el TXST setee gana igual". REFUTADO — ver la REGLA 2 al final de
        ' esta Sub: cuando el MNAM carga, el material es la única fuente y los TX## no se aplican.
        ' forceDiffuseOnly (RE 2026-07-16): la fuente FTST del camino Face FO4 se aplica como el
        ' ATTACH del engine — GetTexturePath(slot 0) ÚNICAMENTE (game 0x1406EE0D7 / CK 0x140ED3830,
        ' todas las llamadas con xor edx,edx). Ni MNAM ni el resto de slots: el attach no carga el
        ' material del TXST (un FTST MNAM-only como SkinSupermutantHead da GetTexturePath(0)=vacío,
        ' medido: el CK bakea NEGRO — experimento TestDftm v1).
        If forceDiffuseOnly Then
            If TxstSlotDecision(textureSet.FormID, "Diffuse", textureSet.DiffuseTexture, material.Diffuse_or_Base_Texture, gatedSlot:=False, diffuseOnly:=True) Then material.Diffuse_or_Base_Texture = textureSet.DiffuseTexture
            Return
        End If

        Dim mnamMaterialApplied As Boolean = False
        If textureSet.MaterialPath <> "" Then
            Dim overrideMaterial = MaterialResolver.TryLoadMaterialFromDictionary(textureSet.MaterialPath, material, shap, nif)
            If overrideMaterial IsNot Nothing Then
                mnamMaterialApplied = True
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
                '
                ' REGLA A — QUÉ SLOTS (restaura el contrato ya documentado arriba en :523-534, que el
                ' cambio "textures-only" de 2026-06-15 había dejado sin implementar):
                '   UsesBodyTexture=True  → full-replace de slots (el HDPT declara "esta parte lleva la
                '                           piel del cuerpo": el BGSM del MNAM ES el material de piel).
                '   UsesBodyTexture=False → SÓLO el diffuse. El MNAM aporta el tinte de superficie de
                '                           esta shape; Normal/SmoothSpec/Envmap/resto quedan del shader
                '                           INLINE del NIF fuente.
                ' EVIDENCIA MEDIDA: NPC Fallout4.esm 0x00002F24 (Valentine), shape
                ' SynthGen2HeadRearValentine, MNAM=gen2skindirty.bgsm. Copiar los 8+ slots pisaba el
                ' Gen2Skin_s y BORRABA el cubemap mipblur_DefaultOutside1_dielectric.dds que traía el
                ' shader inline; el CK sólo reemplazó el diffuse. Consecuencia medida: nuestro texset
                ' quedaba byte-idéntico al de otra shape y el dedupe los colapsaba ⇒ CK 6
                ' BSShaderTextureSet vs nuestros 5 (categoría block-histogram).
                ' ⛔ DEROGADO 2026-07-19: la frase original decía que los TX## del propio TXST se
                ' aplican por encima. La evidencia Valentine NO la sostenía (su TXST es MNAM-only,
                ' sin ningún TX## poblado) y BAKETEST2 N_S/N_S2 la refuta directamente. Ver REGLA 2.
                '
                ' ⭐ CORRECCIÓN 2026-07-19 (bake controlado BAKETEST2FO4.esp) — el material del MNAM
                ' aporta EXACTAMENTE {diffuse} + {normal, smoothSpec si UsesBodyTexture}. NADA MÁS.
                ' MEDIDO: BAKETEST2_N_S (0x843, MNAM=actors\synths\Gen2Skin.BGSM, que declara envmap
                ' Shared/Cubemaps/mipblur_DefaultOutside1_dielectric.dds) y BAKETEST2_N_S2 (0x844,
                ' MNAM=actors\synths\Gen2Eyes.BGSM, que declara envmap mipblur_DefaultOutside1.dds Y
                ' glow Actors/Synths/Gen2Eyes_g.DDS), los dos con UsesBodyTexture=True. El CK horneó
                ' en el head-rear SÓLO Gen2Skin_d/_n/_s y Gen2Eyes_d/_n/_s → TX00/TX01/TX07, y dejó
                ' TX04 (envmap) y TX02 vacíos: NI el cubemap NI el glow del material se escriben.
                ' Copiar los 8 slots extra era además el modo de falla ya medido en Valentine (borraba
                ' el cubemap inline copiando el vacío del material) — ahora las dos evidencias caen
                ' bajo la MISMA regla en vez de contradecirse.
                ' ⚠️ NO MEDIDO directamente: greyscale/wrinkles/specular/lighting/flow/innerLayer/
                ' displacement — ningún material de piel vanilla los declara (verificado sobre
                ' Gen2Skin, Gen2Eyes, Gen2SkinDirty, childfemalebody: sólo D/N/S no vacíos). Se
                ' excluyen por coherencia con la regla medida (el material aporta sólo lo que el CK
                ' escribe) y porque copiarlos vacíos es justamente lo que rompía Valentine.
                material.Diffuse_or_Base_Texture = overrideMaterial.Diffuse_or_Base_Texture
                If usesBodyTexture Then
                    material.NormalTexture = overrideMaterial.NormalTexture
                    material.SmoothSpecTexture = overrideMaterial.SmoothSpecTexture
                End If
                ' REGLA B — QUÉ ALPHA: del MNAM el CK toma ÚNICAMENTE el booleano AlphaTest (ni
                ' alphaBlend ni alphaTestRef: ver la construcción del NiAlphaProperty en
                ' FO4UnifiedMaterial_Class.WriteAlphaPropertyToShape), y SÓLO cuando el TXST es el
                ' FTST declarado A NIVEL NPC de un head part de cara. El default FTST de la RACE NO
                ' gobierna el alpha: ahí el CK se queda con el material inline del mesh.
                ' EVIDENCIA MEDIDA:
                '   · Valentine (Fallout4.esm 0x00002F24) SÍ tiene NPC.FTST → TXST 0x0010C3CD
                '     'SkinHeadValentine' → gen2skinheadvalentine.bgsm (alphaTest=1): CK emite
                '     NiAlphaProperty + flag F4SPF2 Alpha_Test.
                '   · DiMA (DLCCoast.esm 0x00004639) NO tiene NPC.FTST; nuestro resolver caía al FTST
                '     por defecto de la RACE (0x03042EBB → el MISMO TXST 0x0010C3CD de Valentine) y le
                '     aplicaba gen2skinheadvalentine.bgsm ⇒ emitíamos un NiAlphaProperty que el CK NO
                '     emite (el CK usa el material inline del mesh, gen2skinhead.bgsm, alphaTest=0).
                '     Categoría medida: alpha-prop presencia.
                If isFaceHeadPart AndAlso isNpcExplicitFaceTextureSet Then
                    material.AlphaTest = overrideMaterial.AlphaTest
                    ' Portador SEPARADO (no re-leer material.AlphaTest aguas abajo: lo comparte con el
                    ' NiAlphaProperty del mesh fuente). Sólo ESTE productor gobierna el bit F4SPF2
                    ' Alpha_Test; ver Save_To_Shader en FO4UnifiedMaterial_Class.
                    material.AlphaTestFromNpcFtst = overrideMaterial.AlphaTest
                End If
                relatedMaterial.path = FO4UnifiedMaterial_Class.CorrectMaterialPath(textureSet.MaterialPath)
                If logEnabled Then
                    Dim mnamL = If(textureSet.MaterialPath, ""), ubt = usesBodyTexture
                    Logger.LogLazy(Function() $"[TXST-MNAM] mnam='{mnamL}' usesBodyTexture={ubt} → TEXTURES-ONLY (shader del fuente)")
                End If
            End If
        End If

        ' ⭐ REGLA 2 (bake controlado BAKETEST2FO4.esp, 2026-07-19) — CUANDO EL MNAM CARGA, EL MATERIAL
        ' ES LA ÚNICA FUENTE DE PATHS: los TX## del propio TXST NO se aplican por encima.
        ' MEDIDO: BAKETEST2_N_S (0x843) y BAKETEST2_N_S2 (0x844) tienen los OCHO slots del TXST
        ' poblados con rutas exóticas (TX00/01/07=BaseMaleHead_d/_n/_s, TX02=Preston_n, TX03=Mayor_d,
        ' TX04=PiperHead_s, TX05=Chrome_e, TX06=EyeCubeMap) MÁS un MNAM. El CK no escribió NI UNA de
        ' las ocho: horneó Gen2Skin_*/Gen2Eyes_* (del BGSM) en TX00/TX01/TX07 y dejó el resto vacío.
        ' O sea el MNAM gana incluso en los slots donde el TXST tiene valor propio.
        ' Esto DEROGA el comentario previo ("los TX## se aplican por encima"), que no era una medición:
        ' el TXST que lo sostenía (SkinHeadValentine 0x0010C3CD) es MNAM-ONLY — cero TX## poblados —
        ' así que la capa nunca se ejercitó. Verificado sobre el corpus: de 382 TXST en Fallout4.esm
        ' (+129 en los DLC) hay EXACTAMENTE UNO con MNAM y TX## a la vez (0x0006AB32
        ' WallPanelMetalRubble03S, arquitectura, nunca piel ni head part) ⇒ este cambio es INERTE
        ' sobre el corpus vanilla FO4 y sólo puede afectar a mods. En SSE es estructuralmente
        ' inaplicable: el TXST de Skyrim no tiene subrecord MNAM (0 de 572).
        If Not mnamMaterialApplied Then
            ApplyTextureSetToMaterial(material, textureSet, isHeadPartTextureSet, fo4FaceComposeInputsOnly)
        ElseIf logEnabled Then
            Dim tsFid = textureSet.FormID
            Logger.LogLazy(Function() $"[TXST-MNAM] txst=0x{tsFid:X8} → TX## del TXST NO aplicados (el material del MNAM es la única fuente; regla BAKETEST2 N_S/N_S2)")
        End If
    End Sub

    ''' <param name="fo4FaceComposeInputsOnly">⭐ SOLO FO4, cadena FTST del head part de cara. Ver la
    ''' REGLA MEDIDA en el cuerpo (bake controlado BAKETESTFO4.esp).</param>
    Friend Shared Sub ApplyTextureSetToMaterial(material As FO4UnifiedMaterial_Class, textureSet As TXST_Data, Optional isHeadPartTextureSet As Boolean = False, Optional fo4FaceComposeInputsOnly As Boolean = False)
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

        ' ⭐⭐ REGLA FO4 — EL FTST ES *INPUT DEL COMPOSE*, NO UNA LISTA DE PATHS PARA EL NIF.
        ' MEDIDA byte-exacto con el bake controlado BAKETESTFO4.esp (47 NIFs + 282 DDS producidos por el
        ' CK, 2026-07-18):
        '     TX00 → _d      TX01 → _msn      TX07 → _s
        '     TX02, TX03, TX04, TX05, TX06 = INERTES (ni al compose ni al NIF)
        ' y CERO slots del FTST se propagan al NIF horneado: en TX00/01/07 el CK escribe los archivos
        ' GENERADOS FaceCustomization\<plugin>\<FormID>_{d,msn,s}.dds (eso ya lo hace FaceGenBuilder.
        ' BakeFaceTextures, que reescribe los slots 0/1/7) y el resto de los slots del material quedan
        ' como estaban (default de raza / head part).
        ' EVIDENCIA (comparación CRUZADA, no correlación): T7 vs T1 comparten SÓLO TX00 ⇒ _d byte-idéntico;
        ' T7 vs T3 comparten SÓLO TX01 y TX07 ⇒ _msn y _s byte-idénticos. Test diferencial del diffuse:
        ' corr +0,9910 sobre 3,1M canales-píxel. Material: facegen_baseline\ck_experiment_fo4\CK_OUTPUT*.
        ' El flag DNAM del TXST (Facegen 0x0002 / MSN 0x0004) es INERTE de punta a punta: 3 TXST con los
        ' mismos 8 paths y sólo el DNAM distinto producen NIFs Y los 3 canales DDS byte-idénticos — por eso
        ' `isFacegen` NO participa de esta decisión.
        ' ⛔ Por qué esto NO repite la reversión de 2026-07-17: aquella revirtió "FTST diffuse-only" porque
        ' el efecto neto sobre el composite no estaba cerrado en el RE (874 NPCs). La medición de arriba lo
        ' cerró Y refutó las DOS hipótesis previas: "slot 0 únicamente" rompería _msn/_s (TX01 y TX07 SÍ
        ' alimentan el compose) y "los 8 slots" mete 5 slots de ruido (medido: FemaleHeadHuman texslot[5]
        ' quedaba con HeadWrinkles_n del FTST masculino donde el CK deja el BaseFemaleHeadWrinkles_n).
        ' Sólo FO4 y sólo la cadena FTST del head part de cara: el camino Skyrim (ley por capas + shader
        ' type, 0 defectos) no se toca.
        If fo4FaceComposeInputsOnly Then diffuseOnly = False   ' D/N/S sí; los 5 inertes se cortan abajo

        ' ⭐⭐ SSE HEAD PART: el discriminante es el SHADER TYPE AUTORADO del shape, NO el flag DNAM del TXST.
        ' Fuente: bake CK SkyrimSE `FUNC 0x141d0ea00`, switch por shader type @0x141d0ed89:
        '     type 4 FaceTint : N(TX01) + _sk(TX03→LightingTexture) + detail(TX04→DisplacementTexture)
        '     type 5 SkinTint : N(TX01) y NADA más
        '     type 6 HairTint : 0 texturas
        '     cualquier otro  : CERO escrituras
        ' En ningún caso escribe SmoothSpec (el TX07 sólo alimenta un SetShaderFlag, no es slot).
        ' ⛔ El gate viejo `diffuseOnly = isHeadPart AndAlso Not isFacegen` es un HEURÍSTICO derivado de FO4
        ' (origen xEdit wbDefinitionsFO4.pas:7350). El bit facegen NO lo lee ningún applier de material en
        ' ningún motor — sólo código de editor/preview (ver reference_txst_facegen_msn_not_engine_gate).
        ' Coincidía en vanilla y mispredecía justo donde el TXST corrige al mesh.
        ' MEDIDO vanilla limpio: los Marks* humanos (cicatrices) usan maskleftside.nif / maskrightside.nif,
        ' shType=SkinTint(5) — NO son los *scar*.nif de las razas bestia, que sí son Default y a los que el CK
        ' efectivamente no les escribe nada. Para 'MarksMaleHumanoid06LeftGash' (NPC 0x00013261) el CK horneó
        ' Normal='Actors\Character\Male\FaceDetails\FaceLeftSideGash06_n.dds' (del TXST) y nosotros dejábamos el
        ' inline 'FaceLeftSideGash05_n.dds' porque su TXST no lleva el flag DNAM 0x0002. El Diffuse coincide en
        ' ambos, así que el slot 0 no se toca acá. 645 NPCs / 933 shapes afectados.
        Dim sseHeadPart As Boolean =
            isHeadPartTextureSet AndAlso
            Config_App.Current IsNot Nothing AndAlso Config_App.Current.Game = Config_App.Game_Enum.Skyrim
        Dim allowNormal As Boolean = True, allowSk As Boolean = True, allowDetail As Boolean = True
        If sseHeadPart Then
            Select Case material.NifShaderType
                Case NiflySharp.Enums.BSLightingShaderType.FaceTint
                    allowNormal = True : allowSk = True : allowDetail = True
                Case NiflySharp.Enums.BSLightingShaderType.SkinTint
                    allowNormal = True : allowSk = False : allowDetail = False
                Case Else
                    ' HairTint(6) y todo lo demás (ojos EyeEnvmap, Default, EnvironmentMap…): cero texturas.
                    allowNormal = False : allowSk = False : allowDetail = False
            End Select
            If logEnabled Then
                Dim stL = material.NifShaderType, aN = allowNormal, aSkL = allowSk, aD = allowDetail, fidL = textureSet.FormID
                Logger.LogLazy(Function() $"[TXST-APPLY] SSE head part shType={stL} → ley CK 0x141d0ea00: N={aN} _sk={aSkL} detail={aD} S=never (txst=0x{fidL:X8})")
            End If
        End If
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
        ' En SSE head part la ley del CK manda sobre el heurístico diffuseOnly (ver bloque de arriba).
        If TxstSlotDecision(txstFid, "Normal", textureSet.NormalTexture, material.NormalTexture, gatedSlot:=True, diffuseOnly:=If(sseHeadPart, Not allowNormal, diffuseOnly)) Then material.NormalTexture = textureSet.NormalTexture
        ' Wrinkles / Envmap / InnerLayer: la ley del CK (0x141d0ea00) no los escribe para NINGÚN shader type ⇒
        ' en head part SSE quedan siempre del material inline del mesh.
        If TxstSlotDecision(txstFid, "Wrinkles", textureSet.WrinklesTexture, material.WrinklesTexture, gatedSlot:=True, diffuseOnly:=If(sseHeadPart OrElse fo4FaceComposeInputsOnly, True, diffuseOnly)) Then material.WrinklesTexture = textureSet.WrinklesTexture
        ' Glow slot (TXST TX03). FO4 = emissive glow. SSE = "Glow/Detail Map" (wbDefinitionsTES5.pas:5588) que
        ' para piel/cara ES el _sk (subsurface). Debe ir a LightingTexture (subsurface, engine t12), NO al slot
        ' emisivo — espejo EXACTO de FO4UnifiedMaterial.ReadBgsmTexturesFromTextureSet (game-aware). FO4 sin cambios.
        If TxstSlotDecision(txstFid, "Glow", textureSet.GlowTexture, material.GlowTexture, gatedSlot:=True, diffuseOnly:=If(sseHeadPart, Not allowSk, If(fo4FaceComposeInputsOnly, True, diffuseOnly))) Then
            Dim isSseTxst As Boolean = (Config_App.Current IsNot Nothing AndAlso Config_App.Current.Game = Config_App.Game_Enum.Skyrim)
            If isSseTxst AndAlso Not material.Glowmap AndAlso (material.SubsurfaceLighting OrElse material.RimLighting OrElse material.Facegen OrElse material.SkinTint) Then
                material.LightingTexture = textureSet.GlowTexture
                material.GlowTexture = ""
            Else
                material.GlowTexture = textureSet.GlowTexture
            End If
        End If
        If TxstSlotDecision(txstFid, "Height", textureSet.HeightTexture, material.DisplacementTexture, gatedSlot:=True, diffuseOnly:=If(sseHeadPart, Not allowDetail, If(fo4FaceComposeInputsOnly, True, diffuseOnly))) Then material.DisplacementTexture = textureSet.HeightTexture
        If TxstSlotDecision(txstFid, "Envmap", textureSet.EnvironmentTexture, material.EnvmapTexture, gatedSlot:=True, diffuseOnly:=If(sseHeadPart OrElse fo4FaceComposeInputsOnly, True, diffuseOnly)) Then material.EnvmapTexture = textureSet.EnvironmentTexture
        If TxstSlotDecision(txstFid, "InnerLayer", textureSet.MultilayerTexture, material.InnerLayerTexture, gatedSlot:=True, diffuseOnly:=If(sseHeadPart OrElse fo4FaceComposeInputsOnly, True, diffuseOnly)) Then material.InnerLayerTexture = textureSet.MultilayerTexture
        ' ⭐⭐ SSE head part: el TX07 NO es un slot de textura para el motor — NADIE escribe el slot 7.
        ' Fuente: bake CK `0x141d0ea00` (type 4 FaceTint escribe slot1/slot2/slot3/slot6; el `txst[7]` sólo
        ' alimenta un SetShaderFlag, no un path) y runtime: el attach `0x14042BAA0` escribe SÓLO el slot 0 y
        ' el regen `0x14042BD90` sólo N/_sk/detail. Es decir el specular que el motor USA es el que quedó en
        ' el NIF = el INLINE del mesh. Al pisarlo con el TX07 del TXST cambiábamos el specular real, en el
        ' render Y en el bake (por eso el fix va acá, en el resolver compartido, y no en el escritor del NIF).
        ' MEDIDO vanilla limpio (CK del BSA, sin mods): 2461 shapes / 1792 NPCs, ej. MaleHeadDremora
        ' texslot[7] nuestro='Actors\Character\Male\MaleHead_S.dds' (string del TXST) vs
        ' CK='textures\actors\character\male\MaleHead_S.dds' (inline del mesh).
        ' ⛔ REEMPLAZA la nota de FaceGenBuilder (2026-07-09) que lo declaró "no-op visual, misma textura con
        ' otro path": NO es no-op — con cualquier TNAM que difiera del inline (mods) cambia la textura usada.
        ' Ver reference_txst_facegen_msn_not_engine_gate. Sólo SSE y sólo head parts: en FO4 el slot 7 es el
        ' _s de FaceCustomization que el CK sí escribe, y fuera de head parts (cuerpo/armadura) el TXST manda.
        Dim sseHeadPartNoTx07 As Boolean =
            isHeadPartTextureSet AndAlso
            Config_App.Current IsNot Nothing AndAlso Config_App.Current.Game = Config_App.Game_Enum.Skyrim
        If sseHeadPartNoTx07 Then
            If logEnabled Then
                Dim fidL7 = txstFid, curL7 = If(material.SmoothSpecTexture, ""), txL7 = If(textureSet.SmoothSpecTexture, "")
                Logger.LogLazy(Function() $"[TXST-SLOT] txst=0x{fidL7:X8} slot=SmoothSpec txstPath='{txL7}' → skip:SSE-HEADPART-TX07-NOT-A-SLOT (kept inline='{curL7}')")
            End If
        ElseIf TxstSlotDecision(txstFid, "SmoothSpec", textureSet.SmoothSpecTexture, material.SmoothSpecTexture, gatedSlot:=True, diffuseOnly:=diffuseOnly) Then
            material.SmoothSpecTexture = textureSet.SmoothSpecTexture
        End If
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
                ' SSE RaceMenu absolute hair tint (packed 0xRRGGBB from the applied .jslot) — precedence over the
                ' CLFM, matching skee's ApplyMappedPreset which writes the preset's hairColor straight onto the hair
                ' shader material. The ×2 SSE doubling is applied downstream in ApplyMaterialPaletteHairColor, exactly
                ' as it is for the CLFM colour, so this stays a single hair-tint resolution point.
                If state IsNot Nothing AndAlso state.SseHairColorRgb.HasValue Then
                    Dim rgb = state.SseHairColorRgb.Value
                    Return Color.FromArgb((rgb >> 16) And &HFF, (rgb >> 8) And &HFF, rgb And &HFF)
                End If
                ' ⭐ HDPT.CNAM (Head Part Color) GANA sobre NPC.HCLF, por head part. Antes este Select salía
                ' con el HCLF y el `headPartColor` de abajo quedaba INALCANZABLE para pelo/barba/cejas — o sea
                ' justo para los únicos head parts donde el CNAM existe.
                ' MEDIDO sobre vanilla+DLC: hay EXACTAMENTE 5 HDPT con CNAM<>0 en todo el juego, las cinco de
                ' Serana/Valerica y todas apuntando a 0x000A0434 HairColor11Black:
                '   0x0200D95D DLC1HairFemaleSerana · 0x0200D95C DLC1HairLineFemaleSerana
                '   0x020029A9 DLC1HairFemaleValerica · 0x020029AA DLC1HairLineFemaleValerica
                '   0x0200E88C DLC1HairFemaleSeranaHuman (variante no equipada)
                ' ⇒ 4 shapes horneadas en 2 NPCs, que es exactamente la categoría que quedaba abierta.
                ' Verificado byte a byte contra el CK: pelo y hairline de ambas dan (52,56,56) = 2×(26,28,28) del
                ' CNAM; nosotros dábamos el HCLF del NPC — (40,40,48) en Valerica, (32,36,36) en Serana. Sus
                ' CEJAS coinciden en ambos lados porque NO tienen CNAM y caen al HCLF, lo que confirma la
                ' precedencia y descarta que fuera un problema del ×2 (el factor es correcto en los dos lados).
                ' ⛔ El gate es `CNAM <> 0`, NO el flag DATA 0x10 "Use Solid Tint": ninguna de las 5 lo tiene
                ' seteado y el CK usó el CNAM igual.
                ' ⚠️ SIN MEDIR: la precedencia relativa entre SseHairColorRgb (preset RaceMenu) y HDPT.CNAM —
                ' no hay ningún caso en el corpus vanilla que la ejercite. Se deja el preset primero, que es el
                ' comportamiento previo.
                If headPartColor.HasValue Then Return headPartColor
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
        ' state.RaceFormID = raza EFECTIVA (override del editor): sin él, npcData sin preset es el raw
        ' cacheado y la rama SSE derivaría el QNAM del catálogo de la raza VIEJA tras un cambio de raza.
        Return NpcRecordOverlay.DeriveSkinToneQnam(npcData, race, state.IsFemale, _ctx.PluginManager,
                                                   raceFormIDOverride:=state.RaceFormID)
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
        ' FO4-only (ghoul races / GhoulFemaleBody / FemaleHeadHumanRearTEMP son conceptos FO4). El race-set
        ' de abajo ya no matchearía en SSE, pero el gate explícito lo deja inequívoco y evita cualquier clone.
        If Config_App.Current.Game <> Config_App.Game_Enum.Fallout4 Then Return False
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

        ' Final body D/N/S exactly as the body resolves them. MISMA PRECEDENCIA que
        ' ApplyTextureSetOverrides (RENDER == BAKE, un solo camino de resolución): si el MNAM carga,
        ' el material es la ÚNICA fuente y los TX## del TXST NO se aplican encima (REGLA 2,
        ' BAKETEST2 N_S/N_S2). Sólo sin MNAM (o si el BGSM no carga) mandan los TX## del TXST.
        ' Inerte sobre el corpus vanilla: no hay ningún TXST de piel con MNAM y TX## a la vez.
        Dim srcD As String = ""
        Dim srcN As String = ""
        Dim srcS As String = ""
        Dim bodyMnamLoaded As Boolean = False
        If Not String.IsNullOrEmpty(bodyTxst.MaterialPath) Then
            Dim bodyBgsm = LoadVanillaBodyMaterial(bodyTxst.MaterialPath, shape)
            If bodyBgsm IsNot Nothing Then
                bodyMnamLoaded = True
                srcD = If(bodyBgsm.Diffuse_or_Base_Texture, "")
                srcN = If(bodyBgsm.NormalTexture, "")
                srcS = If(bodyBgsm.SmoothSpecTexture, "")
            End If
        End If
        If Not bodyMnamLoaded Then
            If Not String.IsNullOrEmpty(bodyTxst.DiffuseTexture) Then srcD = bodyTxst.DiffuseTexture
            If Not String.IsNullOrEmpty(bodyTxst.NormalTexture) Then srcN = bodyTxst.NormalTexture
            If Not String.IsNullOrEmpty(bodyTxst.SmoothSpecTexture) Then srcS = bodyTxst.SmoothSpecTexture
        End If

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

        ' RaceMenu skin overrides (Skyrim only) — resolved once for this candidate and applied IN PLACE to each
        ' skin shape at the tail of the loop below (skee NIOVTaskUpdateTexture: replace only the override's slots).
        ' Nothing on FO4 (SseSkinOverrides is never populated there), so the FO4 path is untouched.
        Dim sseSkinOverrides As List(Of RaceMenuJslot.JslotSkinOverride) = Nothing
        If _appliedPresets IsNot Nothing AndAlso state IsNot Nothing AndAlso Config_App.Current.Game = Config_App.Game_Enum.Skyrim Then
            Dim ssePreset As LooksmenuLoader.LooksmenuPreset = Nothing
            If _appliedPresets.TryGetValue(state.RootNpcFormID, ssePreset) AndAlso ssePreset IsNot Nothing _
               AndAlso ssePreset.SseSkinOverrides IsNot Nothing AndAlso ssePreset.SseSkinOverrides.Count > 0 Then
                sseSkinOverrides = ssePreset.SseSkinOverrides
            End If
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
        Dim isFaceTxstSource As Boolean = False
        Dim sseFaceAux As TXST_Data = Nothing
        Dim fo4FaceComposeOnly As Boolean = False
        Dim textureSet = ResolveTextureSet(candidate, state, isFaceTxstSource, sseFaceAux, fo4FaceComposeOnly)

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

            ' ENGINE-FAITHFUL (Fallout4.exe, ver memoria project_arma_skin_txst_engine): la piel de un
            ' shape SkinTint SIEMPRE sale del skin del ACTOR (WNAM del NPC ?? RACE → skin ARMO → ARMA
            ' NAM0 male/NAM1 female por sexo), NO del NAM0/1 del ARMA del propio outfit. Un outfit trae
            ' partes SkinTint que reemplazan el naked body — esas reciben la piel del actor abajo
            ' (actorBodySkinTxst), y la ropa (no-SkinTint) conserva su material. Por eso el NAM0/1 del
            ' ARMA del outfit NO debe aplicarse como override a NINGÚN shape del outfit (hacerlo pintaba
            ' la ropa y metía el male NAM0 sobre una female). HeadPart usa HDPT.TNAM/FTST (otro
            ' mecanismo → se mantiene). Skin (naked body) usa su ARMA, que ES el skin ARMO del WNAM.
            If candidate Is Nothing OrElse candidate.Kind <> MainForm.MeshCandidateKind.Outfit Then
                ' Skin (naked body) candidate: the body-skin TXST (ARMA NAM0/NAM1) must ONLY replace
                ' shapes whose shader is SkinTint — the engine only skins SkinTint geometry (both games,
                ' user rule 2026-07-09). Non-SkinTint shapes bundled inside a body mesh (e.g. CBBE
                ' Bra/Panty underwear = Default shader, or the eyes/mouth in the vanilla all-in-one
                ' childfeet.nif = EyeEnvmap/Default) keep their OWN material — painting them with the
                ' body diffuse was the "skin on underwear / body texture on eyes" bug. FO4 body meshes
                ' are all-SkinTint so this is a no-op there (no FO4 regression). HeadPart candidates are
                ' unaffected: their HDPT.TNAM legitimately applies to the head part regardless of shader.
                Dim isSkinCand As Boolean = (candidate IsNot Nothing AndAlso candidate.Kind = MainForm.MeshCandidateKind.Skin)
                Dim shaderIsSkinTint As Boolean = (matPre IsNot Nothing AndAlso matPre.NifShaderType = NiflySharp.Enums.BSLightingShaderType.SkinTint)
                ' ENGINE-FAITHFUL face gate (RE 2026-07-16, AMBOS binarios): el motor aplica el texture
                ' set de cara (NPC.FTST / RACE.DFTM-DFTF) SOLO a shapes cuyo material es shader-type
                ' Face(4) — SSE runtime RegenerateHead 0x14042BD90 (gate GetType()==4 en 0x14042BF0C) y
                ' FO4 CK resolver de bake 0x140ED41F6 (gate sub esi,4/je en 0x140ED437D). Un HDPT Face
                ' cuyo shape NO es material Face conserva sus texturas autoradas (caso vanilla SSE
                ' ManakinRace: MaleHeadManekin → ManekinHead.nif shader=Default queda con su madera;
                ' sin este gate se le pisaba el diffuse con FemaleHead.dds del DFTF). mat.Facegen es el
                ' predicado game-aware (SSE: IsTypeFaceTint / FO4: flag Face, FO4UnifiedMaterial:3183).
                ' HDPT.TNAM NO se gatea: el engine lo aplica al attachear el modelo del head part,
                ' independiente del shader (ojos EnvMap, etc.). Este gate corre en render Y bake (el
                ' bake usa este mismo Sub vía delegate — BakeAllRunner:336).
                Dim shaderIsFace As Boolean = (matPre IsNot Nothing AndAlso matPre.Facegen)
                If isFaceTxstSource AndAlso textureSet IsNot Nothing AndAlso Not shaderIsFace Then
                    If logEnabled Then
                        Dim shN2 = shape.ShapeName
                        Dim shTy2 = If(matPre IsNot Nothing, matPre.NifShaderType.ToString(), "?")
                        Logger.LogLazy(Function() $"[FACE-SHADER-GATE] face TXST source (FTST): shape='{shN2}' shader={shTy2} Facegen=False → face texture set NOT applied (keeps authored material).")
                    End If
                ElseIf (Not isSkinCand) OrElse shaderIsSkinTint Then
                    ' isFaceTxstSource (solo FO4, =FTST): diffuse-only per RE del attach (slot 0).
                    ApplyTextureSetOverrides(textureSet, relatedMaterial, candidate.UsesBodyTexture, shape.NifShape, shape.NifContent,
                                             isHeadPartTextureSet:=(candidate IsNot Nothing AndAlso candidate.Kind = MainForm.MeshCandidateKind.HeadPart),
                                             isFaceHeadPart:=(candidate IsNot Nothing AndAlso candidate.HeadPartType = HeadPartTypeFace),
                                             forceDiffuseOnly:=isFaceTxstSource,
                                             isNpcExplicitFaceTextureSet:=(state IsNot Nothing AndAlso state.ExplicitHeadTextureFormID <> 0UI AndAlso
                                                                           textureSet IsNot Nothing AndAlso textureSet.FormID = state.ExplicitHeadTextureFormID),
                                             fo4FaceComposeInputsOnly:=fo4FaceComposeOnly)
                ElseIf logEnabled Then
                    Dim shN = shape.ShapeName
                    Dim shTy = If(matPre IsNot Nothing, matPre.NifShaderType.ToString(), "?")
                    Logger.LogLazy(Function() $"[SKIN-SHADER-GATE] Skin candidate: shape='{shN}' shader={shTy} (not SkinTint) → body TXST NOT applied (keeps own material).")
                End If

                ' CAPA AUX SSE (modelo por capas, ver doc en ResolveTextureSet): sobre la base TNAM ya
                ' aplicada, el set resuelto FTST??DFT??TNAM aporta SOLO Normal(TX01) + _sk(TX03→
                ' LightingTexture, mismo remap SSE que el slot Glow en ApplyTextureSetToMaterial: en
                ' un material Face/Facegen el TX03 es subsurface, no emisivo) + detail(TX04→
                ' DisplacementTexture, el que consume el fold del facetint). NI diffuse NI SmoothSpec
                ' (TX07 solo togglea el flag specular en el motor — path queda del TNAM; Razhinda
                ' shipped: S=FemaleHead_s del TNAM). Gate por material Face(4) = mat.Facegen, igual
                ' que RegenerateHead 0x14042BF0C. Slot vacío del aux ⇒ conserva el de la base (el
                ' motor solo carga la textura si el path del slot resuelve — 0x14042C45C/0x14042C08E).
                ' Corre en render Y bake (Sub compartido vía delegate).
                If sseFaceAux IsNot Nothing AndAlso relatedMaterial.material IsNot Nothing Then
                    Dim mAux = relatedMaterial.material
                    Dim auxShaderIsFace As Boolean = (matPre IsNot Nothing AndAlso matPre.Facegen)
                    If auxShaderIsFace Then
                        Dim appliedN = False, appliedSk = False, appliedDet = False
                        If Not String.IsNullOrEmpty(sseFaceAux.NormalTexture) Then
                            mAux.NormalTexture = sseFaceAux.NormalTexture : appliedN = True
                        End If
                        If Not String.IsNullOrEmpty(sseFaceAux.GlowTexture) Then
                            mAux.LightingTexture = sseFaceAux.GlowTexture : appliedSk = True
                        End If
                        If Not String.IsNullOrEmpty(sseFaceAux.HeightTexture) Then
                            mAux.DisplacementTexture = sseFaceAux.HeightTexture : appliedDet = True
                        End If
                        If logEnabled Then
                            Dim shNA = shape.ShapeName, aFid = sseFaceAux.FormID
                            Dim aN = appliedN, aSk = appliedSk, aDet = appliedDet
                            Logger.LogLazy(Function() $"[FACE-AUX-TXST] shape='{shNA}' aux=0x{aFid:X8} → N={aN} sk={aSk} detail={aDet} (D y S quedan de la base TNAM)")
                        End If
                    ElseIf logEnabled Then
                        Dim shNA2 = shape.ShapeName
                        Dim shTyA = If(matPre IsNot Nothing, matPre.NifShaderType.ToString(), "?")
                        Logger.LogLazy(Function() $"[FACE-AUX-TXST] shape='{shNA2}' shader={shTyA} Facegen=False → capa aux NO aplicada (gate Face).")
                    End If
                End If
            End If

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
                Dim skinMnamLoaded As Boolean = False
                If actorBodySkinTxst.MaterialPath <> "" Then
                    Dim bgsmMaterial = MaterialResolver.TryLoadMaterialFromDictionary(actorBodySkinTxst.MaterialPath, material, shape.NifShape, shape.NifContent)
                    If bgsmMaterial IsNot Nothing Then
                        skinMnamLoaded = True
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
                ' REGLA 2 (BAKETEST2 N_S/N_S2), misma que ApplyTextureSetOverrides: si el MNAM cargó,
                ' el material es la única fuente de paths y los TX## del TXST NO van encima.
                ' Inerte sobre corpus vanilla (ningún TXST de piel tiene MNAM y TX## a la vez).
                If Not skinMnamLoaded Then
                    If logEnabled Then
                        Dim shapeSubL = shape.ShapeName
                        Logger.LogLazy(Function() $"[SKINSUB] shape='{shapeSubL}' SkinTint en Outfit → sustituye texturas por body skin del actor (TXST slots, sin MNAM)")
                    End If
                    ApplyTextureSetToMaterial(material, actorBodySkinTxst)
                End If
            End If

            ' [SSE-MSN diagnostic] Para CADA shape de outfit con shader SkinTint: si el render lo tratará
            ' como model-space o tangent (materialBase.ModelSpaceNormals) + la normal que quedó. Cubre
            ' también el caso en que la piel del actor no resolvió (skinSubstituted=False).
            If logEnabled AndAlso candidate IsNot Nothing AndAlso candidate.Kind = MainForm.MeshCandidateKind.Outfit _
               AndAlso material.NifShaderType = NiflySharp.Enums.BSLightingShaderType.SkinTint Then
                Dim shN = shape.ShapeName
                Dim msnF = material.ModelSpaceNormals
                Dim stF = material.SkinTint
                Dim nrmF = If(material.NormalTexture, "")
                Dim subbed = (actorBodySkinTxst IsNot Nothing)
                Logger.LogLazy(Function() $"[SSE-MSN] outfit SkinTint shape='{shN}' SkinTint={stF} ModelSpaceNormals={msnF} normal='{nrmF}' skinSubstituted={subbed}")
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

            ' RaceMenu skin override — in-place per-slot texture-set replacement on the skin shape. Runs LAST so a
            ' deliberate user override wins. Membership mirrors the render's decal check: a skin shape (SkinTint /
            ' FaceGen) whose worn biped SlotMask intersects the override's slotMask. Faithful to skee's
            ' NIOVTaskUpdateTexture (replace only the override's slots; key 7 tint → SkinTintColor; key 8 → Alpha).
            If sseSkinOverrides IsNot Nothing AndAlso candidate IsNot Nothing _
               AndAlso (material.NifShaderType = NiflySharp.Enums.BSLightingShaderType.SkinTint OrElse material.SkinTint) Then
                For Each sk In sseSkinOverrides
                    If sk Is Nothing OrElse sk.SlotMask = 0UI Then Continue For
                    ' skee SkinOverrideApplicator (OverrideInterface.cpp:1080): the shape's biped slot mask must
                    ' CONTAIN every bit of the override's slotMask (superset), not merely intersect.
                    If (candidate.SlotMask And sk.SlotMask) <> sk.SlotMask Then Continue For
                    ApplySseSkinOverrideToMaterial(material, sk)
                Next
            End If

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

                ' [SHADER-CMP] Dump COMPLETO del shader/material por shape para comparar outfit-SkinTint vs body.
                ' Incluye Kind (Outfit/Skin/HeadPart), TODOS los flags de shader y los colores/escalares. Standalone
                ' en WM se ve bien → comparar estos campos body-vs-outfit revela qué mete distinto nuestro proceso.
                Dim kindCmp = If(candidate IsNot Nothing, candidate.Kind.ToString(), "?")
                Dim shN2 = shapeNameFinal
                Dim fSkin = material.SkinTint, fFace = material.Facegen, fHair = material.Hair
                Dim fGlow = material.Glowmap, fSss = material.SubsurfaceLighting, fRim = material.RimLighting
                Dim fBack = material.BackLighting, fMsn = material.ModelSpaceNormals, fTwo = material.TwoSided
                Dim fSpecEn = material.SpecularEnabled, fEnv = material.EnvironmentMapping, fEyeEnv = material.EyeEnvironmentMapping
                Dim cSkinR = material.SkinTintColor.R, cSkinG = material.SkinTintColor.G, cSkinB = material.SkinTintColor.B, cSkinA = material.SkinTintAlpha
                Dim cHair = material.HairTintColor, cEmit = material.EmittanceColor, cEmitM = material.EmittanceMult
                Dim cSpec = material.SpecularColor, cSpecM = material.SpecularMult
                Dim sGloss = material.Smoothness, sSssRoll = material.SubsurfaceLightingRolloff, sPalScale = palScaleFinal
                Logger.LogLazy(Function() $"[SHADER-CMP] shape='{shN2}' KIND={kindCmp} shader={shaderFinal} | SkinTint={fSkin} Facegen={fFace} Hair={fHair} Glowmap={fGlow} Sss={fSss} Rim={fRim} Back={fBack} MSN={fMsn} TwoSided={fTwo} SpecEn={fSpecEn} EnvMap={fEnv} EyeEnv={fEyeEnv}")
                Logger.LogLazy(Function() $"[SHADER-CMP] shape='{shN2}' KIND={kindCmp} | SkinTintColor=({cSkinR},{cSkinG},{cSkinB}) SkinTintAlpha={cSkinA:F3} HairTint=({cHair.R},{cHair.G},{cHair.B}) Emit=({cEmit.R},{cEmit.G},{cEmit.B})x{cEmitM:F2} Spec=({cSpec.R},{cSpec.G},{cSpec.B})x{cSpecM:F2} gloss={sGloss:F2} sssRoll={sSssRoll:F3}")
            End If
        Next
    End Sub

End Class
