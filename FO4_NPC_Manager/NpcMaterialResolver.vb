Imports System.Globalization
Imports System.IO
Imports System.Drawing
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports FO4_Base_Library
Imports FO4_Base_Library.Canon.CanonInterpretacion
Imports MaterialLib
Imports NiflySharp
Imports NiflySharp.Blocks
Imports OpenTK.Mathematics

''' <summary>Phase 2 of the MainForm split: material / texture-set / hair-palette / color-form
''' resolution extracted from MainForm into a standalone class (DI via NpcRenderContext). Increment 1
''' = resolver/leaf core; ApplyShapeMaterialOverrides + skin-tone resolvers stay in MainForm for
''' later increments. See 61-perf-mainform-split.</summary>
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

    ' HDPT PNAM Type values (same FO4 schema mapping as MainForm).
    Private Const HeadPartTypeFace As Integer = 1
    Private Const HeadPartTypeHair As Integer = 3
    Private Const HeadPartTypeFacialHair As Integer = 4

    Private Shared _txstFlagDumpDone As Boolean = False

    ''' <summary>Carga el material del cuerpo desde sus bytes VANILLA (BA2), saltando cualquier override
    ''' suelto: la malla de nuca de UV vanilla necesita la textura vanilla, no la que apunte el BGSM suelto
    ''' de un mod. GetOverriddenEntries devuelve los perdedores tapados por el suelto; el primero con
    ''' IsLosseFile=False es el del BA2, y se parsea con el MISMO parser de la cadena viva. Sin entrada
    ''' overrideada no hay suelto, así que el resolver normal ya devuelve vanilla.</summary>
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

    ''' <summary>Resolución engine-faithful de paleta y HairTintColor para head parts de pelo. FUENTE ÚNICA:
    ''' la usan la carga del NIF y el refresh live del preset (estuvo duplicada con guards distintos, y el laxo
    ''' pintaba cualquier material con paleta habilitada).
    ''' <para>Regla del motor: el <c>RemappingIndex</c> del CLFM lo consumen solo los head parts a los que el
    ''' motor equipa un color form — pelo, barba y cejas. Con RemappingIndex y LUT resoluble se activa la
    ''' paleta; si no, cae al HairTintColor. No-op silencioso fuera de ese caso.
    ''' Ver 50-facetint-leyes-y-compositor §A.7.</para></summary>
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

        ' SSE RaceMenu absolute hair tint (.jslot actor.hairColor, packed 0xRRGGBB) — resolved HERE, the one
        ' place BOTH entry points share, instead of in ResolveHairTintColor (which only the NIF-load path calls
        ' and which therefore left the live-preview refresh — NpcFaceTintResolver.RefreshFaceTintLivePreview,
        ' which passes hairTintColorOverride:=Nothing — falling back to the CLFM and silently dropping the
        ' preset colour on any TexturesOnly refresh). One resolution point = render, live refresh and BAKE agree.
        ' GAME-GATED: FO4 presets carry no such value and a FO4 hair CLFM works by RemappingIndex, not RGB.
        Dim presetHairRgb As Integer? = Nothing
        If state IsNot Nothing AndAlso Config_App.Current IsNot Nothing AndAlso
           Config_App.Current.Game = Config_App.Game_Enum.Skyrim Then
            presetHairRgb = state.SseHairColorRgb
        End If

        Dim didPalette As Boolean = False
        ' The preset colour is ABSOLUTE and outranks the palette branch (skee overwrites the material's tint
        ' outright, PresetInterface.cpp:112-116). In practice SSE never takes the palette branch anyway — a
        ' Skyrim CLFM carries an RGB, not a RemappingIndex — so this only makes the precedence explicit.
        If hairColorFormID <> 0UI AndAlso Not presetHairRgb.HasValue Then
            Dim clfm = ResolveColorFormData(hairColorFormID)
            If clfm IsNot Nothing AndAlso clfm.TieneIndiceDePaleta() Then
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
                material.GrayscaleToPaletteScale = clfm.IndiceDePaleta()
                Dim palTex As String = ""
                If sourceHadPalette Then
                    ' Priority: BGSM's own GreyscaleTexture first (per-shape, picked by the stylist
                    ' for THIS mesh), RACE.HNAM/HLTX as fallback. The engine in-game binds the LUT
                    ' from the material's TXST slot 3 at render time (F4SE CharGenInterface.cpp:
                    ' 1106-1179, ProcessHairColor → SetTextureFilename(3, ...)). Vanilla
                    ' HumanChildRace ships without HNAM/HLTX precisely because the BGSM carries it.
                    palTex = If(material.GreyscaleTexture, "")
                    If palTex = "" Then palTex = ResolveRaceHairLookupTexture(state, _ctx.PluginManager)
                    ' f4ee ProcessHairColor (CharGenInterface.cpp:1126-1172): si el color de pelo del NPC
                    ' está registrado en un LUTs\<plugin>\haircolors.json Y la paleta actual es elegible
                    ' (= la gradient vanilla, o ya una LUT registrada), el motor clona el TXST y pisa la
                    ' RANURA 3 con la LUT del JSON. Sin esto, N colores que comparten fila se ven idénticos
                    ' — el mod de 512 colores tiene sólo 16 filas distintas. La FILA no cambia: el
                    ' GrayscaleToPaletteScale de arriba sigue siendo el RemappingIndex del CLFM.
                    LmHairColorLutLoader.EnsureLoaded(_ctx.PluginManager, _ctx.DataPath)
                    palTex = LmHairColorLutLoader.ApplyCustomLutMesh(palTex, hairColorFormID)
                    If palTex <> "" Then
                        material.GrayscaleToPaletteColor = True
                        material.GreyscaleTexture = palTex
                    End If
                End If
                ' La rama palette manejó el material (escribió el scale) → no caer al HairTintColor
                ' fallback, que pisaría el HairTintColor de la fuente (CK no lo cambia en barbas OFF).
                didPalette = True
                If logEnabled Then
                    Dim newScale = clfm.IndiceDePaleta()
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
            ' Preset RGB first: it is what skee paints on the live material, so it outranks both the HDPT.CNAM
            ' solid tint and the NPC's CLFM. (SIN MEDIR, as before: no vanilla case exercises preset-vs-CNAM,
            ' since CNAM exists on exactly 5 HDPT in all of vanilla+DLC. Preset-first was the prior behaviour.)
            If presetHairRgb.HasValue Then
                effectiveHairColor = Color.FromArgb((presetHairRgb.Value >> 16) And &HFF,
                                                    (presetHairRgb.Value >> 8) And &HFF,
                                                    presetHairRgb.Value And &HFF)
            End If
            ' HDPT.CNAM (Head Part Color) GANA sobre NPC.HCLF, por head part. Se resuelve ACÁ —punto
            ' compartido render+bake— porque el camino del RENDER llama con hairTintColorOverride:=Nothing
            ' (NpcFaceTintResolver.vb:1184) y sin esto vería el HCLF mientras el bake ve el CNAM: RENDER ≠ BAKE.
            ' MEDIDO: sólo 5 HDPT en todo vanilla+DLC tienen CNAM<>0 (pelo y hairline de Serana y Valerica,
            ' todas → 0x000A0434 HairColor11Black). CK hornea (52,56,56) = 2×(26,28,28) del CNAM; nosotros
            ' dábamos el HCLF del NPC. Sus cejas (sin CNAM) coinciden en ambos lados y caen al HCLF, lo que
            ' confirma que la precedencia es por head part y que el ×2 estaba bien.
            ' Gate = `CNAM <> 0`, NO el flag DATA 0x10 "Use Solid Tint" (ninguna de las 5 lo tiene y el CK
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
                ' SYNC: RENDER == BAKE. Convención SSE de hair tint: el motor usa el color del CLFM DOBLADO
                ' (CK.HairTintColor == 2 x CLFM.Color, medido byte-exacto). Punto único que consumen render y bake.
                ' El x2 va por TintColorScale (dominio FLOAT), NO doblando los bytes: el storage del material
                ' son 3 bytes ⇒ techo 255 = 1.0, y con un canal >=128 el doblado en bytes clampeaba. El shader
                ' tolera tint > 1. Se escribe siempre (1.0F fuera de SSE) para no arrastrar estado al reutilizar
                ' el material. FO4 no pasa por acá: usa la rama de grayscale-palette. Ver 31-sse-color-de-pelo-clfm.
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

    ''' <summary>Devuelve el material del shape a su estado AUTORADO (el del NIF/BGSM), re-derivándolo
    ''' con <c>Nifcontent_Class_Manolo.GetRelatedMaterial</c> — la MISMA función que llama el ctor de
    ''' <c>NifRenderableShape</c> (:47), así que el resultado es por construcción idéntico al de un shape
    ''' recién construido.
    '''
    ''' POR QUÉ RE-DERIVAR Y NO GUARDAR UN SNAPSHOT: el estado del shape no es el material, es el PAR
    ''' <c>(path, material)</c> — <c>ShapeMaterialOverrides.ApplyMaterialSwap</c> (:124-125) reescribe LOS
    ''' DOS. Un snapshot de sólo el material dejaría el `path` del swap anterior, y el MSWP no volvería a
    ''' matchear (su sustitución es A→B y el path ya diría B) ⇒ el swap se perdería en silencio, con
    ''' `EnsureShapeMaterialResolved` (:117) memoizando el par inconsistente. `GetRelatedMaterial` devuelve
    ''' el par entero, así que la re-derivación no tiene ese agujero. De paso evita depender de la fidelidad
    ''' de <c>FO4UnifiedMaterial_Class.Clone()</c> (que copia el underlying por reflexión + una lista
    ''' CERRADA de campos del wrapper) y no agrega ningún campo a <c>RelatedMaterial_Class</c>, que es un
    ''' tipo COMPARTIDO con Wardrobe_Manager.
    '''
    ''' SE MUTAN LOS DOS CAMPOS DEL WRAPPER EXISTENTE, no se reemplaza el wrapper: hay código que guarda
    ''' referencias al <c>RelatedMaterial_Class</c> (capas de overlay, <c>Render.vb OverrideRelatedMaterial</c>).
    ''' Es exactamente el mismo movimiento que ya hace <c>ApplyMaterialSwap</c> en cada render.
    '''
    ''' SÓLO ES SEGURO SI DESPUÉS CORRE LA LEY COMPLETA (<see cref="ApplyShapeMaterialOverrides"/>): esto
    ''' borra TODOS los overrides del shape, no sólo el que se quiera recalcular. Restaurar sin re-aplicar
    ''' deja el shape en crudo.
    '''
    ''' Verificado puro: <c>Create_From_Shader</c> construye un BGSM/BGEM nuevo y sólo LEE el shader;
    ''' <c>EnsureShaderGameType</c> (:3242) sale temprano si el tipo ya está fijado (idempotente); el render
    ''' no fabrica ni borra bloques del NIF (los 3 call sites de <c>WriteAlphaPropertyToShape</c> son el
    ''' guardado de WM y <c>Save_To_Shader</c> del bake/export, ninguno en el render).
    '''
    ''' False cuando falta NifContent/NifShape/ShapeMaterial o la re-derivación no produce material — el
    ''' caller DEBE caer a la recarga completa, nunca adivinar.</summary>
    Friend Shared Function TryRestoreAuthoredMaterial(shape As IRenderableShape) As Boolean
        If shape Is Nothing Then Return False
        Dim rel = shape.ShapeMaterial
        If rel Is Nothing Then Return False
        Dim nif = shape.NifContent
        Dim nifShape = shape.NifShape
        If nif Is Nothing OrElse nifShape Is Nothing Then Return False

        Dim fresh = nif.GetRelatedMaterial(nifShape)
        If fresh Is Nothing OrElse fresh.material Is Nothing Then Return False

        rel.path = fresh.path
        rel.material = fresh.material
        Return True
    End Function

    ''' <summary>Resuelve el TXST del body skin del actor (NPC.WNAM o RACE.WNAM via state.SkinFormID),
    ''' diferenciando por región: BODY (torso/legs) o HAND. El engine in-game sustituye la diffuse
    ''' texture de los shapes con BSLightingShaderType.SkinTint por la del actor — esto permite a
    ''' un mismo .nif outfit (autoreado con texturas embebidas humanas) verse correcto sobre ghoul,
    ''' synth, super mutant, etc. La sustitución debe usar la textura body (NakedTorso ARMA) para
    ''' shapes con piel del torso/brazos/legs y la hand (NakedHands ARMA) para shapes en gloves
    ''' con piel expuesta de manos.
    ''' Retorna Nothing si state.SkinFormID no resuelve a un ARMO con ARMA gender-correct válida.</summary>
    ''' <param name="shapeSlotMask">Opcional: bits (slot−30) de las particiones BSDismember del SHAPE que va
    ''' a recibir la piel (<see cref="ShapeBipedSlotMask"/>). Sólo desempata dentro del conjunto que la ley
    ''' de región ya aceptó, y sólo en la pasada race-válida — ver <see cref="SelectArmatureForShape"/>.
    ''' 0 (default) = comportamiento histórico. Los call sites que resuelven 1×candidate (head part
    ''' UsesBodyTexture, head-rear ghoul) lo dejan en 0 A PROPÓSITO: corren ANTES del loop de shapes y son
    ''' el camino que consume el BAKE por delegate, así que pasarles máscara cambiaría la salida horneada.</param>
    Friend Function ResolveActorSkinTextureSet(state As MainForm.NPCVisualState, region As MainForm.SkinRegion,
                                               Optional shapeSlotMask As UInteger = 0UI) As Canon.ITxst
        If state Is Nothing OrElse state.SkinFormID = 0UI Then Return Nothing

        Dim armo = _ctx.GetParsedArmo(state.SkinFormID)
        If armo Is Nothing Then Return Nothing

        ' Máscaras de slot GAME-AWARE (FO4 vs SSE difieren: FO4 Body=slot33/bit3, Hands=slots34-35;
        ' SSE Body=slot32/bit2, Hands=slot33/bit3). Fuente única = BipedSlots._fo4Regions/_sseRegions.
        ' Sin esto, en SSE el bit3 (=Hands) matcheaba el viejo BODY_BIT → la ARMA de MANOS se elegía
        ' para la región Body y el cuerpo del outfit recibía la textura de manos.
        Dim bodyMask As UInteger = BipedSlots.RegionMask(BipedSlots.BipedRegion.Body)
        Dim handMask As UInteger = BipedSlots.RegionMask(BipedSlots.BipedRegion.Hands)

        ' Para el skin ARMO la raza es una PREFERENCIA, no un gate: el CK no filtra por raza las ARMA del
        ' ARMO de piel (medido con bake controlado). Tiene sentido, el ARMO viene del WNAM ⇒ ES la piel del
        ' actor por construcción. Pero el gate no se puede borrar: hay skin ARMO que listan varias ARMA de
        ' body de razas distintas y la primera es la equivocada (un YaoGuai tomaría la de sabueso). De ahí las
        ' DOS pasadas: la 1 exige race-match y la 2 —sólo si en la 1 NINGÚN armature race-válido CUBRIÓ LA
        ' REGIÓN— acepta cualquiera de la región. Es estrictamente ADITIVA. Subdeterminado con varias ARMA
        ' non-matching: toma la primera, sin caso vanilla que lo discrimine.
        ' "no cubrió la región" ≠ "no trajo TXST": ver el bloque del selector más abajo. Un armature
        ' race-válido que cubre la región CIERRA la resolución aunque su TXST sea 0 — no se cae al pass 2.
        For pass As Integer = 0 To 1
            Dim requireRaceMatch As Boolean = (pass = 0)

            ' S = los armatures que cubren la REGIÓN en esta pasada, EN ORDEN DE armo.ArmorAddons.
            Dim s As New List(Of Canon.IArma)()
            For Each entry In ArmoEditor_Form.ReadAddons(armo)
                Dim a = _ctx.GetParsedArma(entry.ArmaFormID)
                If a Is Nothing Then Continue For
                ' ArmaMatchesRace vive en EquipResolver (Records\, no se toca): sigue pidiendo el
                ' modelo legado, así que se puentea acá nomás.
                If requireRaceMatch AndAlso
                   Not EquipResolver.ArmaMatchesRace(a,
                                                     state.RaceFormID, _ctx.GetEffectiveArmorRaces(state.RaceFormID)) Then Continue For
                Dim armaSlot = a.SlotMaskDe()

                Dim matches As Boolean = False
                Select Case region
                    Case MainForm.SkinRegion.Body
                        matches = (armaSlot And bodyMask) <> 0UI
                    Case MainForm.SkinRegion.Hand
                        matches = (armaSlot And handMask) <> 0UI AndAlso (armaSlot And bodyMask) = 0UI
                End Select
                If matches Then s.Add(a)
            Next
            If s.Count = 0 Then Continue For

            ' LA PASADA 2 VA CON MÁSCARA 0, A PROPÓSITO — no es otra ley, es un INPUT declarado.
            ' La pasada 2 no es la ley del motor: el motor filtra los addons WORN, y que un skin ARMO no
            ' liste ningún armature para la raza es una configuración rota. La pasada 2 es una heurística
            ' NUESTRA (regla BAKETEST2 N_D1/R_D1) que ya elige arbitrariamente, así que refinarla con datos
            ' fieles al motor no la vuelve fiel: la vuelve DISTINTA. Y costaría inercia: medido
            ' (`sweep_v3.py`, condición suficiente) el desempate cambiaría 6 combinaciones de la pasada 2
            ' en SSE (0 en FO4), todas en las 3 razas cuyo ARMO no las sirve — `TestRace` de Dawnguard y
            ' `00UBE_CustomRace01/02`, ninguna con NPC_ que la referencie directo. NO medimos cuál de las
            ' dos elecciones es mejor; elegimos la INERCIA, y el gate reporta la pasada 2 para el día que
            ' alguien quiera decidirlo con datos.
            Dim arma = SelectArmatureForShape(s, If(requireRaceMatch, shapeSlotMask, 0UI))

            ' EL ARMATURE ELEGIDO **ES** EL SELECTOR: acá se DECIDE, no se sigue
            ' buscando. Ley del motor (getter de skin TXST 0x140a90790 / thunk 0x14004e693, ver
            ' 23-armor-arma-skin-txst): itera los addons worn, se queda con el que matchea la región y lee
            ' SU `[arma+sex*8+0x240]`; si ese slot es null, el shape conserva SU PROPIA textura
            ' (`[prop+0xb0]`). El motor NUNCA cae a OTRO armature buscando uno que sí tenga TXST.
            '
            ' Acá había `Continue For` y ESE era el bug de UBE (medido 2026-08-15, log + records):
            ' `00UBE_SkinNaked` (0x0D0144E7) lista 25 armatures; los 3 de UBE (00UBE_NakedTorso/Feet/Hands)
            ' traen las texturas DENTRO del NIF y dejan NAM0=NAM1=0, así que el scan se los salteaba y
            ' seguía por los 21 armatures vanilla que el ARMO hereda → devolvía `SkinBodyFemaleChild`
            ' (0x0007E5CF: `FemaleChild\UpperBodyFemale.dds` + `MaleChild\UpperBodyMale_n/_sk`) y la pintaba
            ' sobre las UV de UBE en los shapes SkinTint del outfit — encima con normal MSN vanilla sobre un
            ' mesh `*_tangent_*.nif`. El cuerpo desnudo NO se veía afectado porque ése va por
            ' `candidate.TextureSetFormID` (=0 ⇒ ApplyTextureSetOverrides hace Return y conserva lo autorado):
            ' los dos caminos resolvían la piel con reglas distintas y por eso divergían.
            '
            ' Barrido old-vs-new DE ESTE CAMBIO (el del `Continue For`, no el del desempate per-slot —
            ' no confundir con las cifras de SelectArmatureForShape, que miden OTRA cosa): sobre todos
            ' los skin ARMO referenciados por un RACE.WNAM de Skyrim.esm + UBE_AllRace.esp, 460
            ' combinaciones (raza × región × sexo), 64 diferencias, y las 64 son del ARMO de UBE ⇒
            ' INERTE sobre vanilla.
            '
            ' Fallback EXACTO del motor (getter 0x140a90790: [arma+sex*8+0x240], null→índice0=NAM0/male):
            ' female → NAM1, si vacío → NAM0 (male). male → NAM0 (sin fallback a female).
            ' Confirmado por el bake: BAKETEST2_N_G (0x846) tiene ARMA_G con SÓLO NAM0 y NPC femenino
            ' → el CK horneó OldHumanMaleHead_* (o sea female cae a NAM0).
            Dim txstFID = If(state.IsFemale,
                             If(arma.FemaleSkinTexture <> 0UI, arma.FemaleSkinTexture, arma.MaleSkinTexture),
                             arma.MaleSkinTexture)
            If txstFID = 0UI Then
                If Logger.Enabled Then
                    Dim aEid0 = arma.EditorID, rFid0 = state.RaceFormID, regL0 = region.ToString()
                    Logger.LogLazy(Function() $"[SKINTXST-NOSLOT] region={regL0}: el armature '{aEid0}' (race del actor 0x{rFid0:X8}) la cubre pero NO declara skin TXST (NAM0=NAM1=0) → SIN sustitución; el shape conserva su textura autorada (ley del motor: selector null ⇒ textura propia)")
                End If
                Return Nothing
            End If

            Dim txstRec = _ctx.PluginManager.GetRecord(txstFID)
            If txstRec Is Nothing OrElse txstRec.Header.Signature <> "TXST" Then
                If Logger.Enabled Then
                    Dim aEid1 = arma.EditorID, tFid1 = txstFID, regL1 = region.ToString()
                    Logger.LogLazy(Function() $"[SKINTXST-NOSLOT] region={regL1}: el armature '{aEid1}' declara skin TXST 0x{tFid1:X8} pero NO resuelve a un record TXST → SIN sustitución (misma ley que el slot null)")
                End If
                Return Nothing
            End If

            If Logger.Enabled AndAlso Not requireRaceMatch Then
                Dim aEid = arma.EditorID, rFid = state.RaceFormID
                Logger.LogLazy(Function() $"[SKINTXST-RACEFALLBACK] ninguna ARMA race-válida cubrió la región; se acepta '{aEid}' (race del actor 0x{rFid:X8}) — regla BAKETEST2 N_D1/R_D1")
            End If
            Return Canon.CanonRecords.Txst(txstRec, _ctx.PluginManager)
        Next

        Return Nothing
    End Function

    ''' <summary>Elige, dentro del conjunto <paramref name="candidates"/> que la ley de REGIÓN ya aceptó, el
    ''' armature cuyo SlotMask intersecta el slot REAL del shape (su partición BSDismember). Desempate, no
    ''' filtro: el conjunto no se amplía ni se reduce, sólo cambia CUÁL de sus miembros se devuelve.
    '''
    ''' Motivo: la región Body de SSE agrupa los slots 32+37+38+40 (<c>BipedSlots.BuildSseRegions</c>), así
    ''' que sin esto un armature de PIES puede terminar siendo el selector del TXST de un shape de TORSO. El
    ''' motor no hace eso: su getter de skin TXST filtra los addons worn con un predicado POR SLOT
    ''' (<c>[arma_subobj+0x40].vfn(0xa8)(slot)</c>).
    '''
    ''' "EL PRIMERO QUE INTERSECTA" ESTÁ ATADO AL ARNÉS DEL GATE. El barrido de no-regresión enumera
    ''' bits de slot ÚNICOS, y eso es exhaustivo SÓLO por esta regla: siendo `idx(b)=min{i : S[i] cubre b}`,
    ''' para cualquier máscara M vale `winner(M) = S[min_{b∈B(M)} idx(b)]` ⇒ los ganadores alcanzables con
    ''' máscara arbitraria son un SUBCONJUNTO de los alcanzables con un solo bit (probado, y brute-forceado:
    ''' 428.752 máscaras multi-bit en SSE + 229.616 en FO4, 0 ganadores fuera del set de 1 bit). Si alguien
    ''' cambia esto a "el que cubre más bits" o "mayor solapamiento", la enumeración por bit único deja de
    ''' ser exhaustiva y el gate MIENTE EN SILENCIO. Cambiar una obliga a cambiar el otro.
    '''
    ''' Inercia MEDIDA hoy (`sweep_v3.py`, condición suficiente, predicado de raza con la cadena RACE.RNAM
    ''' completa, universo = ARMO por RACE.WNAM ∪ NPC_.WNAM, regiones Body y Hand, ambos sexos):
    ''' pasada 1 = **516 combinaciones en SSE y 366 en FO4, 0 cambios reales** en las dos. (Conteo verificado
    ''' contra un arnés independiente del revisor, que dio los mismos 516/366.)
    '''
    ''' Esto NO implementa el selector per-slot del motor, sólo se acerca: la REGIÓN del candidate sigue
    ''' decidiendo el conjunto antes del desempate (el lumping Body-vs-Hand sigue vivo) y la pasada 2 va con
    ''' máscara 0. No leer esto como "ya está el selector del motor".</summary>
    ''' <param name="shapeSlotMask">Bits (slot−30) de las particiones del shape; 0 = sin dato ⇒ se conserva
    ''' el primer cubridor de la región, que es el comportamiento histórico.</param>
    Friend Shared Function SelectArmatureForShape(candidates As List(Of Canon.IArma), shapeSlotMask As UInteger) As Canon.IArma
        If candidates Is Nothing OrElse candidates.Count = 0 Then Return Nothing
        If shapeSlotMask <> 0UI Then
            For Each a In candidates
                If (a.SlotMaskDe() And shapeSlotMask) <> 0UI Then Return a
            Next
        End If
        Return candidates(0)
    End Function

    ''' <summary>Máscara (bit i = biped slot 30+i) de las particiones BSDismember del shape. 0 cuando el
    ''' shape no trae dismember o el NIF no se puede leer — y 0 significa "sin dato", que hace caer a
    ''' <see cref="SelectArmatureForShape"/> en el comportamiento histórico. El plegado 2xx/1xx→base es la
    ''' ley compartida <c>BipedSlots.FoldPartitionBodyPart</c>; el filtro [30,61] es de este call site.
    ''' <para>Usa <c>IRenderableShape.NifSkin</c> —el accesor de la INTERFAZ, que ya hace el deref del skin
    ''' instance con sus guardas— en vez de re-derefear `NifShape.SkinInstanceRef` sobre `NifContent.Blocks`:
    ''' mismo argumento que el del fold, no hace falta una tercera copia del mismo deref. Bonus: funciona
    ''' también para las shapes OSP de Wardrobe_Manager, que implementan la interfaz sin ser NifRenderableShape.</para></summary>
    Friend Shared Function ShapeBipedSlotMask(shape As IRenderableShape) As UInteger
        If shape Is Nothing Then Return 0UI
        Try
            Dim dism = TryCast(shape.NifSkin, NiflySharp.Blocks.BSDismemberSkinInstance)
            If dism Is Nothing OrElse dism.Partitions Is Nothing Then Return 0UI
            Dim m As UInteger = 0UI
            For Each p In dism.Partitions
                Dim v = BipedSlots.FoldPartitionBodyPart(CInt(p.BodyPart))
                If v >= 30 AndAlso v <= 61 Then m = m Or (1UI << (v - 30))
            Next
            Return m
        Catch
            Return 0UI
        End Try
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

    ''' <summary>Proyección candidate → flags de ApplyTextureSetOverrides.
    ''' <para>Su razón de ser ORIGINAL ya no existe: nació para que el render completo y el fast path del
    ''' picker de piel derivaran los flags con UNA sola definición, porque el fast path llamaba
    ''' `ApplyTextureSetToMaterial` pelado (flags en False) y se salteaba el dispatch SSE por shader type y el
    ''' skip de TX07 ⇒ RENDER ≠ RENDER. Ese fast path se BORRÓ: hoy restaura el material autorado y corre
    ''' `ApplyShapeMaterialOverrides`, o sea la ley entera, así que ya no deriva flags por su cuenta. La
    ''' proyección se conserva porque el render completo la usa (:1499) y porque mantiene `HeadPartTypeFace`
    ''' privado acá — replicar esa constante en un caller sería recrear el drift que la motivó.</para></summary>
    Friend Shared Function IsHeadPartTextureSetFor(candidate As MainForm.MeshCandidate) As Boolean
        Return candidate IsNot Nothing AndAlso candidate.Kind = MainForm.MeshCandidateKind.HeadPart
    End Function

    ''' <summary>Ver <see cref="IsHeadPartTextureSetFor"/>: misma proyección compartida render-completo /
    ''' fast path, para el flag isFaceHeadPart.</summary>
    Friend Shared Function IsFaceHeadPartFor(candidate As MainForm.MeshCandidate) As Boolean
        Return candidate IsNot Nothing AndAlso candidate.HeadPartType = HeadPartTypeFace
    End Function

    ' BORRADO 2026-07-21: IsNpcExplicitFaceTextureSetFor + el parámetro isNpcExplicitFaceTextureSet de
    ' ApplyTextureSetOverrides. Era el PROXY REFUTADO del alpha de la cabeza ("el NPC declara su cara por
    ' FTST"): coincidía con Valentine/DiMA por casualidad sobre un corpus de 2 casos, y el árbitro real es
    ' el flag ACBS "Diffuse Alpha Test" (0x01000000) — RE CreationKit 0x140ED41F6, gate [npc+0x9b]&1;
    ' confirmado también en el esquema del record ACBS. Al reemplazarlo por HeadDiffuseAlphaTest
    ' quedó como parámetro que los dos call-sites calculaban y pasaban pero que el CUERPO NO
    ' LEÍA NUNCA. Ver
    ' 40-bake-leyes-fo4.

    Friend Function ResolveHeadPartSolidTintColor(candidate As MainForm.MeshCandidate) As Nullable(Of Color)
        If candidate Is Nothing OrElse Not candidate.UseSolidTint Then Return Nothing
        Return ResolveColorFormColor(candidate.HeadPartColorFormID)
    End Function

    ''' <param name="isFaceTextureSource">Solo FO4: el TXST retornado es el NPC.FTST del camino Face y el
    ''' caller lo aplica DIFFUSE-ONLY (el attach del motor aplica solo el slot 0 de la cadena FTST>body>TNAM).
    ''' Siempre False en SSE. Ver 30-fo4-pipeline-textura-de-cara.</param>
    ''' <param name="sseFaceAuxTextureSet">Solo SSE + head part de cara: el set resuelto (FTST ?? DFT ?? TNAM)
    ''' que aporta unicamente Normal + <c>_sk</c> + detail POR ENCIMA de la base TNAM, gateado por material Face;
    ''' el diffuse jamas se toca. Ley por capas: 50-facetint-leyes-y-compositor B.3.</param>
    ''' <param name="fo4FaceComposeInputsOnly">Solo FO4 + head part de cara, para las TRES procedencias del set
    ''' (FTST, RACE.DFT y el TNAM propio): la ley no depende de que record lo aporto. True => el caller aplica
    ''' solo TX00/TX01/TX07, las entradas del compose. Ver 40-bake-leyes-fo4 seccion 9.</param>
    Friend Function ResolveTextureSet(candidate As MainForm.MeshCandidate, state As MainForm.NPCVisualState, ByRef isFaceTextureSource As Boolean, ByRef sseFaceAuxTextureSet As Canon.ITxst, ByRef fo4FaceComposeInputsOnly As Boolean) As Canon.ITxst
        isFaceTextureSource = False
        sseFaceAuxTextureSet = Nothing
        fo4FaceComposeInputsOnly = False
        Dim logEnabled = Logger.Enabled
        ' Regla canónica de TXST por head part (flags HDPT.DATA):
        '   A) sin TNAM, sin UsesBodyTexture → Nothing (queda lo embebido del NIF).
        '   B) con TNAM, sin UsesBodyTexture → TNAM.
        '   C) UsesBodyTexture → body TXST del actor (SkinFormID → NakedTorso ARMA → TXST del género). La
        '      cadena es race-specific: un HDPT compartido entre razas renderiza distinto según la del NPC.
        ' Caso Face sin TNAM: fallback a NPC.FTST. Se gatea por HeadPartTypeRaw, NO por el efectivo: los
        ' sub-parts Misc que heredan Face vía HNAM (MouthShadow, lashes/AO/wet) conservan su material propio.
        ' Sólo aplica a HeadPart; skin y outfit tienen su propio flujo.
        If candidate IsNot Nothing AndAlso candidate.Kind = MainForm.MeshCandidateKind.HeadPart Then
            ' Caso C: UsesBodyTexture=True gana sobre TNAM.
            If candidate.UsesBodyTexture AndAlso state IsNot Nothing Then
                Dim bodyTxst = ResolveActorSkinTextureSet(state, MainForm.SkinRegion.Body)
                If bodyTxst IsNot Nothing Then
                    If logEnabled Then
                        Dim bFid = bodyTxst.FormID, bMnam = If(bodyTxst.MaterialDe(), "")
                        Dim bD = If(bodyTxst.Ranura(0), ""), bN = If(bodyTxst.Ranura(1), ""), bS = If(bodyTxst.Ranura(7), "")
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
            ' Resolución Face por JUEGO, dos leyes medidas (ver doc del parámetro sseFaceAuxTextureSet):
            '   FO4 — el set resuelto (FTST > TNAM > DFTM si no hay TNAM) reemplaza la BASE COMPLETA D/N/S del
            ' composite FaceCustomization. SIN MEDIR: el RE del CK 0x140ED4244 sugiere DFT>TNAM también con
            '   TNAM presente; no aplicado, cero casos vanilla para validarlo contra un bake.
            '   SSE — modelo POR CAPAS: base = TNAM (D y S siempre del TNAM) y capa aux con solo N/_sk/detail.
            ' El guard usa HeadPartTypeRaw, no el efectivo: protege los sub-parts Misc heredados como Face
            ' (MouthShadow/AO/lashes/wet), que conservan su propio material.
            If candidate.Kind = MainForm.MeshCandidateKind.HeadPart AndAlso candidate.HeadPartTypeRaw = HeadPartTypeFace AndAlso state IsNot Nothing Then
                Dim isSse As Boolean = (Config_App.Current IsNot Nothing AndAlso Config_App.Current.Game = Config_App.Game_Enum.Skyrim)
                If isSse Then
                    ' SSE — MODELO POR CAPAS, validado byte contra el corpus completo de facegeom vanilla:
                    '   D    = TNAM del head part (FTST/DFT JAMÁS pisan el diffuse)
                    '   N / _sk / detail = del RESUELTO
                    '   S y el resto     = AUTORADO del NIF (nadie los escribe)
                    ' resuelto = FTST > DFT[sexo propio] > TNAM. La capa aux aporta N/_sk/detail SÓLO cuando
                    ' el resuelto DIFIERE del TNAM (o sea, cuando hay FTST o DFT); si no hay ninguno de los
                    ' dos, resuelto == TNAM y entonces el TNAM se aplica COMPLETO (incluye TX03=_sk), no
                    ' diffuse-only — por eso `isFaceTextureSource` va True sólo si existe la capa aux.
                    ' Ley completa y evidencia: 50-facetint-leyes-y-compositor.md §B.3.
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
                            sseFaceAuxTextureSet = Canon.CanonRecords.Txst(auxRec, _ctx.PluginManager)
                            If logEnabled Then
                                Dim aSrc = auxSource, aP = sseFaceAuxTextureSet
                                Logger.LogLazy(Function() $"[TXST-RESOLVE] source={aSrc} txst=0x{aP.FormID:X8} eid='{If(aP.EditorID, "")}' → capa SSE N/_sk/detail (base TNAM=diffuse-only) N='{If(aP.Ranura(1), "")}' sk='{If(aP.Ranura(3), "")}' det='{If(aP.Ranura(4), "")}'")
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
                    ' isFaceTextureSource (= forceDiffuseOnly, "slot 0 únicamente") va False acá A
                    ' PROPÓSITO: es una hipótesis REFUTADA por bake controlado — rompería el _msn y el _s
                    ' de 874 NPCs, porque TX01 y TX07 SÍ alimentan el compose. La regla medida es la
                    ' intermedia: TX00/TX01/TX07 al compose, TX02-TX06 inertes.
                    If state.ExplicitHeadTextureFormID <> 0UI Then
                        textureSetFormID = state.ExplicitHeadTextureFormID
                        txstSource = "NPC.FTST(Face-override)"
                        fo4FaceComposeInputsOnly = True
                    ElseIf textureSetFormID = 0UI AndAlso state.HeadTextureFormID <> 0UI Then
                        textureSetFormID = state.HeadTextureFormID
                        txstSource = "RACE.DFTM(Face-fallback)"
                        fo4FaceComposeInputsOnly = True
                    ElseIf textureSetFormID <> 0UI Then
                        ' El TNAM del propio head part de CARA también es "input del compose": la ley no
                        ' depende de qué record aportó el set, sino de que ESTE sea el texture set de la
                        ' cara. Sus D/N/S alimentan el compose de FaceCustomization y ningún otro slot del
                        ' TXST participa (los slots 4 y 5 del facegeom del CK salen del MATERIAL, verbatim).
                        ' NO normalizar esos paths: el CK escribe el string tal cual lo tipeó el autor del
                        ' material, y el femenino usa '.DDS' en mayúscula. Normalizar rompería las cabezas
                        ' femeninas, que hoy coinciden.
                        ' textureSetFormID ya ES el TNAM: no se reasigna nada, sólo se MARCA como input.
                        txstSource = "HDPT.TNAM(Face-compose)"
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

        Dim parsed = Canon.CanonRecords.Txst(rec, _ctx.PluginManager)
        If logEnabled Then
            Dim srcL2 = txstSource, pEid = If(parsed.EditorID, ""), pMnam = If(parsed.MaterialDe(), "")
            Dim pD = If(parsed.Ranura(0), ""), pN = If(parsed.Ranura(1), ""), pS = If(parsed.Ranura(7), ""), pW = If(parsed.Ranura(2), "")
            ' DNAM flags: 0x0001 NoSpecularMap, 0x0002 FacegenTextures, 0x0004 HasModelSpaceNormal.
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
    Friend Shared Sub ApplyTextureSetOverrides(textureSet As Canon.ITxst, relatedMaterial As Nifcontent_Class_Manolo.RelatedMaterial_Class, usesBodyTexture As Boolean, shap As NiflySharp.INiShape, nif As Nifcontent_Class_Manolo, Optional isHeadPartTextureSet As Boolean = False, Optional forceDiffuseOnly As Boolean = False, Optional fo4FaceComposeInputsOnly As Boolean = False, Optional npcDiffuseAlphaTest As Boolean = False)
        If textureSet Is Nothing OrElse relatedMaterial Is Nothing Then Return

        Dim logEnabled = Logger.Enabled
        Dim material = relatedMaterial.material
        If material Is Nothing Then Return

        ' ACÁ ARRIBA Y SIN CONDICIÓN, A PROPÓSITO. Es un HECHO del record del NPC (flag ACBS Diffuse Alpha
        ' Test), no una decisión sobre texturas, así que no puede depender de nada de lo que sigue.
        ' Hasta 2026-08-08 se asignaba DENTRO de la rama del MNAM, ~60 líneas más abajo: si el TXST no traía
        ' MaterialPath, el dato que el call-site había calculado bien NUNCA llegaba al material. Medido:
        ' 1.757 NPC en FO4, y el 100% de SSE — el TXST de Skyrim no tiene subrecord MNAM, así que allá el
        ' portador era estructuralmente inalcanzable. El único síntoma visible era nulo (ver la nota de
        ' NpcDiffuseAlphaTest), pero el bake no podía confiar en su propio dato.
        material.NpcDiffuseAlphaTest = npcDiffuseAlphaTest
        ' El veto de fabricación es el COMPLEMENTO del hecho, tal como lo componía el tri-estado viejo
        ' (True = fabricar + bit; False = vetar + sin bit). Se separan porque son dos cosas distintas, pero
        ' el bake sigue componiéndolas del mismo dato.
        ' CAMBIO DE ALCANCE: al asignarse acá arriba y no dentro de la rama del MNAM, el veto ahora LLEGA
        ' a los NPC cuyo TXST no trae MNAM (1.757 en FO4) y a todo SSE, donde antes se perdía.
        material.VetoAlphaPropertyCreation = Not npcDiffuseAlphaTest

        ' forceDiffuseOnly (RE 2026-07-16): la fuente FTST del camino Face FO4 aplica SOLO el slot 0 — el
        ' ATTACH del engine, GetTexturePath(slot 0) (game 0x1406EE0D7 / CK 0x140ED3830, todas las llamadas
        ' con xor edx,edx).
        '
        ' NO agregar acá una rama que cargue el MNAM: sería código muerto. forceDiffuseOnly sólo se
        ' enciende en la rama SSE, y el TXST de Skyrim no tiene subrecord MNAM (a diferencia de FO4)
        ' ⇒ acá MaterialPath es siempre "".
        ' Los TXST MNAM-only de FO4 entran por el camino normal de más abajo, que ya carga el MNAM.
        If forceDiffuseOnly Then
            If TxstSlotDecision(textureSet.FormID, "Diffuse", textureSet.Ranura(0), material.Diffuse_or_Base_Texture, gatedSlot:=False, diffuseOnly:=True) Then material.Diffuse_or_Base_Texture = textureSet.Ranura(0)
            Return
        End If

        Dim mnamMaterialApplied As Boolean = False
        If textureSet.MaterialDe() <> "" Then
            Dim overrideMaterial = MaterialResolver.TryLoadMaterialFromDictionary(textureSet.MaterialDe(), material, shap, nif)
            If overrideMaterial IsNot Nothing Then
                mnamMaterialApplied = True
                ' TEXTURES-ONLY + ALPHA: el MNAM del TXST aporta SOLO sus paths de textura más el alpha. El
                ' resto del shader (ShaderType/Rolloff/BackLight/Smoothness/Specular/flags) queda del clon del
                ' mesh fuente: el .bgsm es el material runtime y el CK nunca lo hornea en el NIF de FaceGen.
                ' REGLA A — QUÉ SLOTS: exactamente {diffuse} + {normal, smoothSpec} cuando la shape lleva
                ' piel del cuerpo o el TXST es input del compose de cara. Nada más, ni envmap ni glow aunque el
                ' material los declare: copiar los 8 slots borra el cubemap del shader inline.
                material.Diffuse_or_Base_Texture = overrideMaterial.Diffuse_or_Base_Texture
                ' El gate incluye fo4FaceComposeInputsOnly porque en la cadena de cara de FO4 (NPC.FTST,
                ' RACE.DFT o el TNAM del head part) D/N/S no son slots del NIF sino las TRES ENTRADAS del
                ' compose de FaceCustomization, y ahí el CK copia el _s del MNAM (medido en píxeles:
                ' RMS 1,3 contra el MNAM vs 57,9 contra el inline).
                ' NO alcanza con usesBodyTexture NI se puede sacar el gate del todo: para un head part
                ' que NO es la cara el CK toma del MNAM SÓLO el diffuse (verificado en el head-rear de
                ' Valentine, que lleva el _d del MNAM pero el _s del material inline). Son DOS leyes, y
                ' fo4FaceComposeInputsOnly es exactamente el discriminante.
                If usesBodyTexture OrElse fo4FaceComposeInputsOnly Then
                    material.NormalTexture = overrideMaterial.NormalTexture
                    material.SmoothSpecTexture = overrideMaterial.SmoothSpecTexture
                End If
                ' REGLA B — QUÉ ALPHA: el alpha es PARTE DEL MATERIAL, sale del BGSM del MNAM SIN GATE.
                ' El motor (ApplyMaterialToGeometry 0x142169BB0) lo aplica sin mirar la anatomía del shape.
                ' NO re-introducir un gate por anatomía: refutado con contraejemplo vanilla (los ojos de los
                ' Synth Gen1 perdían el alpha-test de su propio material por no ser el head part de cara).
                ' NO borrar ref ni blend acá: la ley del bake no vive en este Sub COMPARTIDO (aplicarla dejó
                ' la cara de Valentine sólida en el preview).
                material.AlphaTest = overrideMaterial.AlphaTest
                material.AlphaTestRef = overrideMaterial.AlphaTestRef
                material.AlphaBlendMode = overrideMaterial.AlphaBlendMode

                relatedMaterial.path = FO4UnifiedMaterial_Class.CorrectMaterialPath(textureSet.MaterialDe())
                If logEnabled Then
                    Dim mnamL = If(textureSet.MaterialDe(), ""), ubt = usesBodyTexture
                    Logger.LogLazy(Function() $"[TXST-MNAM] mnam='{mnamL}' usesBodyTexture={ubt} → TEXTURES-ONLY (shader del fuente)")
                End If
            End If
        End If

        ' REGLA 2 (bake controlado): CUANDO EL MNAM CARGA, EL MATERIAL ES LA ÚNICA FUENTE DE PATHS — los
        ' TX## del propio TXST no se aplican por encima, ni siquiera en los slots donde el TXST trae valor.
        ' Inerte sobre vanilla FO4 (un solo TXST con MNAM y TX## a la vez, y es arquitectura) y estructuralmente
        ' inaplicable en SSE (el TXST de Skyrim no tiene MNAM). Ver 30-fo4-material-vs-nif.
        If Not mnamMaterialApplied Then
            ApplyTextureSetToMaterial(material, textureSet, isHeadPartTextureSet, fo4FaceComposeInputsOnly)
        ElseIf logEnabled Then
            Dim tsFid = textureSet.FormID
            Logger.LogLazy(Function() $"[TXST-MNAM] txst=0x{tsFid:X8} → TX## del TXST NO aplicados (el material del MNAM es la única fuente; regla BAKETEST2 N_S/N_S2)")
        End If
    End Sub

    ''' <param name="fo4FaceComposeInputsOnly">SOLO FO4, texture set del head part de CARA — venga de
    ''' NPC.FTST, de RACE.DFT o del TNAM del propio head part (los tres, medido 2026-07-30). Ver la
    ''' REGLA MEDIDA en el cuerpo (bake controlado BAKETESTFO4.esp).</param>
    Friend Shared Sub ApplyTextureSetToMaterial(material As FO4UnifiedMaterial_Class, textureSet As Canon.ITxst, Optional isHeadPartTextureSet As Boolean = False, Optional fo4FaceComposeInputsOnly As Boolean = False)
        If material Is Nothing OrElse textureSet Is Nothing Then Return

        Dim logEnabled = Logger.Enabled
        ' Slot override gate (FO4): por defecto el TXST pisa TODOS los slots que resuelven. Única excepción
        ' diffuse-only: TXST de HEAD PART sin el flag DNAM 'Facegen Textures' (0x0002) — es un swatch per-part
        ' (color de ojo/boca) y el N/S/env los posee el BGSM del shape. Fuera de head part nunca aplica.
        ' En SSE este heurístico NO manda: lo pisa la ley por shader type del bloque de abajo.
        Dim isFacegen = (textureSet.Flags And &H2US) <> 0US
        Dim diffuseOnly = isHeadPartTextureSet AndAlso Not isFacegen

        ' REGLA FO4 — el FTST es INPUT DEL COMPOSE, no una lista de paths para el NIF:
        '     TX00 → _d   ·   TX01 → _msn   ·   TX07 → _s   ·   TX02-TX06 = INERTES
        ' Ningún slot del FTST se propaga al NIF horneado: en 0/1/7 el CK escribe los archivos GENERADOS de
        ' FaceCustomization (eso lo hace BakeFaceTextures) y el resto queda como estaba.
        ' Las dos hipótesis vecinas están REFUTADAS con datos: "sólo el slot 0" rompe _msn y _s (TX01 y
        ' TX07 SÍ alimentan el compose), y "los 8 slots" mete 5 slots de ruido en el NIF.
        ' El flag DNAM del TXST (Facegen/MSN) es inerte de punta a punta, por eso no participa acá.
        ' Sólo FO4 y sólo la cadena FTST de la cara; el camino de Skyrim va por la ley de capas.
        If fo4FaceComposeInputsOnly Then diffuseOnly = False   ' D/N/S sí; los 5 inertes se cortan abajo

        ' SSE HEAD PART: el discriminante es el SHADER TYPE AUTORADO del shape, NO el flag DNAM del TXST.
        ' Bake CK 0x141d0ea00, switch @0x141d0ed89:
        '     type 4 FaceTint : N(TX01) + _sk(TX03→LightingTexture) + detail(TX04→DisplacementTexture)
        '     type 5 SkinTint : N(TX01) y nada más   ·   type 6 HairTint y cualquier otro: cero escrituras
        ' Nunca escribe SmoothSpec (el TX07 sólo alimenta un SetShaderFlag). El bit facegen del DNAM no lo lee
        ' ningún applier de material en ningún motor: ver 30-fo4-txst-facegen-msn-no-es-gate.
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
            Dim mnamLog = If(textureSet.MaterialDe(), "")
            Dim noSpecL = (textureSet.Flags And &H1US) <> 0US, msnL = (textureSet.Flags And &H4US) <> 0US
            Dim flagsL = textureSet.Flags, hpL = isHeadPartTextureSet, fgL = isFacegen, doL = diffuseOnly
            Logger.LogLazy(Function() $"[TXST-APPLY] txst=0x{txstFid:X8} eid='{txstEid}' flags=0x{flagsL:X4}(facegen={fgL},noSpec={noSpecL},msn={msnL}) headPart={hpL} → diffuseOnly={doL} mnam='{mnamLog}'")
        End If

        ' Diffuse (TX00): nunca se gatea. Resto: se salta solo si diffuseOnly (head-part sin flag Facegen).
        If TxstSlotDecision(txstFid, "Diffuse", textureSet.Ranura(0), material.Diffuse_or_Base_Texture, gatedSlot:=False, diffuseOnly:=diffuseOnly) Then material.Diffuse_or_Base_Texture = textureSet.Ranura(0)
        ' En SSE head part la ley del CK manda sobre el heurístico diffuseOnly (ver bloque de arriba).
        If TxstSlotDecision(txstFid, "Normal", textureSet.Ranura(1), material.NormalTexture, gatedSlot:=True, diffuseOnly:=If(sseHeadPart, Not allowNormal, diffuseOnly)) Then material.NormalTexture = textureSet.Ranura(1)
        ' Wrinkles / Envmap / InnerLayer: la ley del CK (0x141d0ea00) no los escribe para NINGÚN shader type ⇒
        ' en head part SSE quedan siempre del material inline del mesh.
        If TxstSlotDecision(txstFid, "Wrinkles", textureSet.Ranura(2), material.WrinklesTexture, gatedSlot:=True, diffuseOnly:=If(sseHeadPart OrElse fo4FaceComposeInputsOnly, True, diffuseOnly)) Then material.WrinklesTexture = textureSet.Ranura(2)
        ' Glow slot (TXST TX03). FO4 = emissive glow. SSE = "Glow/Detail Map" que
        ' para piel/cara ES el _sk (subsurface). Debe ir a LightingTexture (subsurface, engine t12), NO al slot
        ' emisivo — espejo EXACTO de FO4UnifiedMaterial.ReadBgsmTexturesFromTextureSet (game-aware). FO4 sin cambios.
        If TxstSlotDecision(txstFid, "Glow", textureSet.Ranura(3), material.GlowTexture, gatedSlot:=True, diffuseOnly:=If(sseHeadPart, Not allowSk, If(fo4FaceComposeInputsOnly, True, diffuseOnly))) Then
            Dim isSseTxst As Boolean = (Config_App.Current IsNot Nothing AndAlso Config_App.Current.Game = Config_App.Game_Enum.Skyrim)
            If isSseTxst AndAlso Not material.Glowmap AndAlso (material.SubsurfaceLighting OrElse material.RimLighting OrElse material.Facegen OrElse material.SkinTint) Then
                material.LightingTexture = textureSet.Ranura(3)
                material.GlowTexture = ""
            Else
                material.GlowTexture = textureSet.Ranura(3)
            End If
        End If
        If TxstSlotDecision(txstFid, "Height", textureSet.Ranura(4), material.DisplacementTexture, gatedSlot:=True, diffuseOnly:=If(sseHeadPart, Not allowDetail, If(fo4FaceComposeInputsOnly, True, diffuseOnly))) Then material.DisplacementTexture = textureSet.Ranura(4)
        If TxstSlotDecision(txstFid, "Envmap", textureSet.Ranura(5), material.EnvmapTexture, gatedSlot:=True, diffuseOnly:=If(sseHeadPart OrElse fo4FaceComposeInputsOnly, True, diffuseOnly)) Then material.EnvmapTexture = textureSet.Ranura(5)
        If TxstSlotDecision(txstFid, "InnerLayer", textureSet.Ranura(6), material.InnerLayerTexture, gatedSlot:=True, diffuseOnly:=If(sseHeadPart OrElse fo4FaceComposeInputsOnly, True, diffuseOnly)) Then material.InnerLayerTexture = textureSet.Ranura(6)
        ' SSE head part: el TX07 NO es un slot de textura — nadie escribe el slot 7 (bake CK 0x141d0ea00,
        ' attach 0x14042BAA0, regen 0x14042BD90). El specular que usa el motor es el INLINE del mesh, así que
        ' pisarlo con el TX07 cambiaba el specular real en render Y bake; por eso el gate vive en este resolver
        ' compartido y no en el escritor del NIF. Sólo SSE y sólo head parts: en FO4 el slot 7 es el _s de
        ' FaceCustomization que el CK sí escribe. Ver 40-bake-leyes-sse.
        If sseHeadPart Then
            If logEnabled Then
                Dim fidL7 = txstFid, curL7 = If(material.SmoothSpecTexture, ""), txL7 = If(textureSet.Ranura(7), "")
                Logger.LogLazy(Function() $"[TXST-SLOT] txst=0x{fidL7:X8} slot=SmoothSpec txstPath='{txL7}' → skip:SSE-HEADPART-TX07-NOT-A-SLOT (kept inline='{curL7}')")
            End If
        ElseIf TxstSlotDecision(txstFid, "SmoothSpec", textureSet.Ranura(7), material.SmoothSpecTexture, gatedSlot:=True, diffuseOnly:=diffuseOnly) Then
            material.SmoothSpecTexture = textureSet.Ranura(7)
        End If
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
            Dim t = Canon.CanonRecords.Txst(rec, _ctx.PluginManager)
            Dim fg = (t.Flags And &H2US) <> 0US
            If fg Then facegenCount += 1
            Dim ns = (t.Flags And &H1US) <> 0US, ms = (t.Flags And &H4US) <> 0US
            Dim fid = t.FormID, eid = If(t.EditorID, ""), fl = t.Flags
            Dim hasD = Not String.IsNullOrEmpty(t.Ranura(0))
            Dim hasN = Not String.IsNullOrEmpty(t.Ranura(1))
            Dim hasS = Not String.IsNullOrEmpty(t.Ranura(7))
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
                ' El RGB absoluto de RaceMenu se resuelve en ApplyMaterialPaletteHairColor, no acá: es el
                ' único punto que comparten los dos consumidores (carga del NIF y refresh live), y cuando
                ' estaba acá cualquier refresh de texturas devolvía el pelo al color del CLFM.
                ' HDPT.CNAM gana sobre NPC.HCLF, por head part (verificado contra el CK sobre las únicas 5
                ' head parts de vanilla+DLC que declaran CNAM). El gate es `CNAM <> 0`, NO el flag "Use Solid
                ' Tint": ninguna de las 5 lo tiene y el CK usó el CNAM igual.
                ' SIN MEDIR: la precedencia entre el preset de RaceMenu y el CNAM; se deja el preset primero.
                If headPartColor.HasValue Then Return headPartColor
                Dim hairColor = ResolveColorFormColor(state.HairColorFormID)
                If hairColor.HasValue Then Return hairColor
        End Select

        If headPartColor.HasValue Then Return headPartColor
        Return Nothing
    End Function

    ''' <summary>Path de paleta de pelo efectivo para host + state, con la regla del render: primero el
    ''' <c>GreyscaleTexture</c> de las shapes de PELO cargadas (filtro <c>mat.Hair</c>, no cualquier material
    ''' con paleta — eso matchea armadura recoloreable), si no RACE.HNAM/HLTX. "" si no hay ninguna.
    ''' Fuente única para el swatch de la UI, el override al cargar el NIF y el refresh live del tint.
    ''' El fallback por RACE no alcanza solo: HumanChildRace no declara HNAM/HLTX.</summary>
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

        ' HairColorLookupTexture/HairColorExtendedLookupTexture (HNAM/HLTX con este significado) son
        ' exclusivos de Fallout 4 — Skyrim no los declara en RACE.
        Dim raceFo4 = TryCast(Canon.CanonRecords.Race(raceRec, pluginManager), Canon.RaceFO4)
        If raceFo4 Is Nothing Then Return ""

        Dim lookupCandidates = New String() {raceFo4.HairColorLookupTexture, raceFo4.HairColorExtendedLookupTexture}
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
        If clfm Is Nothing OrElse Not clfm.TieneColor() Then Return Nothing
        Return clfm.ColorDe()
    End Function

    Friend Function ResolveColorFormData(formID As UInteger) As Canon.IClfm
        If formID = 0UI Then Return Nothing

        Dim rec = _ctx.PluginManager.GetRecord(formID)
        If rec Is Nothing OrElse rec.Header.Signature <> "CLFM" Then Return Nothing

        Return Canon.CanonRecords.Clfm(rec, _ctx.PluginManager)
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
    ''' <para>The Slot enum value here is a schema-defined field name, not a hardcoded magic
    ''' number. Returns Nothing when the NPC
    ''' has no layer at the SkinTone slot or the race / CLFM lookup fails.</para></summary>
    Friend Function ResolveNpcSkinToneColor(state As MainForm.NPCVisualState) As Nullable(Of Color)
        Return ResolveNpcSkinToneCore(state, Nothing)
    End Function

    ''' <summary>El mismo tono, pero con el AJUSTE MANUAL del editor de cuerpo ya sumado. Es el tono del CUERPO:
    ''' lo consumen el seed de <c>state.TextureLightingColor</c> (que alimenta TryApplyBodySkinSoftLight) y el
    ''' refresh en vivo. La variante SIN ajuste de arriba es la que sigue leyendo la CARA -si las dos fueran la
    ''' misma, el origen del match se moveria junto con el destino y el ajuste no podria converger nunca.</summary>
    Friend Function ResolveNpcBodySkinToneColor(state As MainForm.NPCVisualState) As Nullable(Of Color)
        Return ResolveNpcSkinToneCore(state, If(state Is Nothing, Nothing, state.SkinToneOffset))
    End Function

    Private Function ResolveNpcSkinToneCore(state As MainForm.NPCVisualState, offset As SkinToneQnamOffset) As Nullable(Of Color)
        If state Is Nothing Then Return Nothing
        Dim modelNpcFormID = NpcStateFactory.FaceAppearanceSourceFormID(state)
        Dim npcData = _overlayResolver(_ctx.GetParsedNpc(modelNpcFormID), state.RootNpcFormID)
        If npcData Is Nothing Then Return Nothing

        Dim raceRec = _ctx.PluginManager.GetRecord(state.RaceFormID)
        If raceRec Is Nothing OrElse raceRec.Header.Signature <> "RACE" Then Return Nothing
        Dim race = _ctx.ParseRaceCanonCached(raceRec)

        ' Single source of truth — same derivation NpcRecordOverlay uses at save time, so the
        ' preview's body skin tone and the persisted ESP's QNAM are guaranteed to agree.
        ' state.RaceFormID = raza EFECTIVA (override del editor): sin él, npcData sin preset es el raw
        ' cacheado y la rama SSE derivaría el QNAM del catálogo de la raza VIEJA tras un cambio de raza.
        Return NpcRecordOverlay.DeriveSkinToneQnam(npcData, race, state.IsFemale, _ctx.PluginManager,
                                                   raceFormIDOverride:=state.RaceFormID,
                                                   offset:=offset)
    End Function

    ' ===== Ghoul female head-rear (nape) vanilla-UV texture clone =====
    ' Bare-id of FemaleHeadHumanRearTEMP (vanilla 0x0004D0E9, PartType 9).
    ' El comentario anterior justificaba la máscara diciendo que "los overrides usan 0x01..0xFF en el byte
    ' alto". Es FALSO: un override CONSERVA el FormID del master, así que el byte alto de este record es
    ' siempre el slot de Fallout4.esm. La máscara es inocua en la práctica (un ESL no puede poseer un object id
    ' de 0x4D0E9, que no entra en 12 bits) pero el razonamiento que la sostenía no era cierto.
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

    ' Memo de sesión para ResolveGhoulHeadRearClonedTextures: el resultado depende SOLO de (RaceFormID,
    ' SkinFormID) y los bytes vanilla del BA2 no cambian en la sesión. Sin esto cada render re-lee el BGSM y
    ' los bytes de textura. Se cachea también el Nothing (fallo determinista). El lock cubre el compute porque
    ' los renders corren en background: dos renders de la misma clave no deben duplicar el clon ni el registro
    ' en el FilesDictionary. No necesita invalidarse por el toggle ApplyGhoulHeadRearFix: ese solo decide si el
    ' clon SE APLICA, aguas arriba de este resolver.
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

    ''' <summary>Para el head-rear de gúl femenina: resuelve los paths D/N/S de la piel del cuerpo del actor y
    ''' clona cada uno a un path suelto distinto bajo <see cref="HeadRearClonedTextureRoot"/> con los bytes
    ''' VANILLA (BA2), registrándolo en el FilesDictionary.
    ''' <para>Por qué: la nuca de UV vanilla necesita textura de UV vanilla, pero con CBBE instalado el suelto
    ''' del path del cuerpo es la textura CBBE-UV. La cache de texturas del render se indexa por string de path
    ''' y es model-wide, así que cuerpo y nuca no pueden tener bytes distintos bajo la misma clave; el clon le
    ''' da a la nuca una clave propia.</para>
    ''' <para>Idempotente (reusa un clon del mismo tamaño). Nothing si no aplica. Ver 30-fo4-materiales-engine-faithful.</para></summary>
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
        If Not String.IsNullOrEmpty(bodyTxst.MaterialDe()) Then
            Dim bodyBgsm = LoadVanillaBodyMaterial(bodyTxst.MaterialDe(), shape)
            If bodyBgsm IsNot Nothing Then
                bodyMnamLoaded = True
                srcD = If(bodyBgsm.Diffuse_or_Base_Texture, "")
                srcN = If(bodyBgsm.NormalTexture, "")
                srcS = If(bodyBgsm.SmoothSpecTexture, "")
            End If
        End If
        If Not bodyMnamLoaded Then
            If Not String.IsNullOrEmpty(bodyTxst.Ranura(0)) Then srcD = bodyTxst.Ranura(0)
            If Not String.IsNullOrEmpty(bodyTxst.Ranura(1)) Then srcN = bodyTxst.Ranura(1)
            If Not String.IsNullOrEmpty(bodyTxst.Ranura(7)) Then srcS = bodyTxst.Ranura(7)
        End If

        Logger.LogLazy(Function() $"[DIAG-HEADREAR] resolve: mnam='{If(bodyTxst.MaterialDe(), "")}' srcD='{srcD}' srcN='{srcN}' srcS='{srcS}'")
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
            ' via the app's MswpDraftResolver instead — it returns the draft's ALREADY-PARSED Canon.IMswp, which we
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
        Dim sseFaceAux As Canon.ITxst = Nothing
        Dim fo4FaceComposeOnly As Boolean = False
        Dim textureSet = ResolveTextureSet(candidate, state, isFaceTxstSource, sseFaceAux, fo4FaceComposeOnly)

        ' Skin substitution per-shape para Outfit: el engine vanilla sustituye la diffuse de shapes
        ' con shader SkinTint dentro de un outfit (escote, brazos expuestos) por la del actor's body
        ' skin (race-specific). Sólo aplica a Outfit. HeadParts usan TXST propio del HDPT (o FaceTint
        ' shader para Face). Skin candidates conservan TXST nativo via ARMA.
        ' La REGIÓN sigue saliendo del candidate (ley actual, sin cambios); lo que pasa a ser PER-SHAPE es el
        ' DESEMPATE dentro del conjunto que esa región acepta — ver SelectArmatureForShape. Memo por máscara
        ' porque si no se re-caminarían los armatures del ARMO (25 en el de UBE) una vez por shape; los
        ' shapes de un mismo outfit comparten máscara casi siempre, así que colapsa a una o dos entradas.
        Dim isOutfitCandidate As Boolean = (candidate IsNot Nothing AndAlso candidate.Kind = MainForm.MeshCandidateKind.Outfit)
        Dim outfitSkinRegion As MainForm.SkinRegion = MainForm.SkinRegion.Body
        If isOutfitCandidate Then outfitSkinRegion = ResolveSkinRegionForOutfit(candidate)
        Dim skinTxstByShapeMask As New Dictionary(Of UInteger, Canon.ITxst)()

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

            ' ENGINE-FAITHFUL (Fallout4.exe, ver memoria 23-armor-arma-skin-txst): la piel de un
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
                ' SYNC: RENDER == BAKE (el bake entra por este mismo Sub vía delegate).
                ' Gate engine-faithful de cara, RE en los dos binarios: el motor aplica el texture set de cara
                ' (NPC.FTST / RACE.DFTM-DFTF) SOLO a shapes con material shader-type Face(4) — SSE 0x14042BF0C,
                ' FO4 CK 0x140ED437D. Un HDPT Face cuyo shape NO es material Face conserva sus texturas
                ' autoradas (ManakinRace: sin el gate se le pisaba la madera con FemaleHead.dds del DFTF).
                ' mat.Facegen es el predicado game-aware. El HDPT.TNAM NO se gatea: el motor lo aplica al
                ' attachear el modelo, independiente del shader.
                Dim shaderIsFace As Boolean = (matPre IsNot Nothing AndAlso matPre.Facegen)
                If isFaceTxstSource AndAlso textureSet IsNot Nothing AndAlso Not shaderIsFace Then
                    If logEnabled Then
                        Dim shN2 = shape.ShapeName
                        Dim shTy2 = If(matPre IsNot Nothing, matPre.NifShaderType.ToString(), "?")
                        Logger.LogLazy(Function() $"[FACE-SHADER-GATE] face TXST source (FTST): shape='{shN2}' shader={shTy2} Facegen=False → face texture set NOT applied (keeps authored material).")
                    End If
                ElseIf (Not isSkinCand) OrElse shaderIsSkinTint Then
                    ' isFaceTxstSource (solo FO4, =FTST): diffuse-only per RE del attach (slot 0).
                    ' La decisión de ESCRITURA del alpha-test se compone ACÁ, no dentro del resolver: es una
                    ' regla del BAKE (dominio NPC). El CK fabrica el NiAlphaProperty y pone el bit Alpha_Test
                    ' SII el NPC tiene ACBS\Diffuse Alpha Test (0x01000000), y solo en el shape de la CARA.
                    ' El False es EXPLÍCITO, no Nothing: es un veto de fabricación, si no los ojos Gen1
                    ' (material con alphaTest=True) estrenarían un NiAlphaProperty que el CK no pone.
                    ' Ver 40-bake-leyes-fo4.
                    ' HECHO del record + anatomía: el bit F4SPF2 Alpha_Test lo pone el CK sólo en la CARA
                    ' de un NPC con el flag ACBS. Es lo único que este dato gobierna desde 2026-08-08 (antes
                    ' también vetaba la creación del NiAlphaProperty; ese veto se borró, ver NpcDiffuseAlphaTest).
                    Dim npcDiffuseAlphaTest As Boolean = state IsNot Nothing AndAlso
                                                        state.HeadDiffuseAlphaTest AndAlso
                                                        IsFaceHeadPartFor(candidate)
                    ApplyTextureSetOverrides(textureSet, relatedMaterial, candidate.UsesBodyTexture, shape.NifShape, shape.NifContent,
                                             isHeadPartTextureSet:=IsHeadPartTextureSetFor(candidate),
                                             forceDiffuseOnly:=isFaceTxstSource,
                                             fo4FaceComposeInputsOnly:=fo4FaceComposeOnly,
                                             npcDiffuseAlphaTest:=npcDiffuseAlphaTest)
                ElseIf logEnabled Then
                    Dim shN = shape.ShapeName
                    Dim shTy = If(matPre IsNot Nothing, matPre.NifShaderType.ToString(), "?")
                    Logger.LogLazy(Function() $"[SKIN-SHADER-GATE] Skin candidate: shape='{shN}' shader={shTy} (not SkinTint) → body TXST NOT applied (keeps own material).")
                End If

                ' SYNC: RENDER == BAKE (Sub compartido vía delegate).
                ' CAPA AUX SSE (modelo por capas, ver doc en ResolveTextureSet): sobre la base TNAM ya aplicada,
                ' el set resuelto aporta SOLO Normal(TX01) + _sk(TX03→LightingTexture: en material Face el TX03
                ' es subsurface, no emisivo) + detail(TX04→DisplacementTexture, el que consume el fold del
                ' facetint). Ni diffuse ni SmoothSpec. Gate por mat.Facegen, igual que RegenerateHead
                ' 0x14042BF0C. Slot vacío del aux ⇒ conserva el de la base: el motor solo carga la textura si
                ' el path del slot resuelve.
                If sseFaceAux IsNot Nothing AndAlso relatedMaterial.material IsNot Nothing Then
                    Dim mAux = relatedMaterial.material
                    Dim auxShaderIsFace As Boolean = (matPre IsNot Nothing AndAlso matPre.Facegen)
                    If auxShaderIsFace Then
                        Dim appliedN = False, appliedSk = False, appliedDet = False
                        If Not String.IsNullOrEmpty(sseFaceAux.Ranura(1)) Then
                            mAux.NormalTexture = sseFaceAux.Ranura(1) : appliedN = True
                        End If
                        If Not String.IsNullOrEmpty(sseFaceAux.Ranura(3)) Then
                            mAux.LightingTexture = sseFaceAux.Ranura(3) : appliedSk = True
                        End If
                        If Not String.IsNullOrEmpty(sseFaceAux.Ranura(4)) Then
                            mAux.DisplacementTexture = sseFaceAux.Ranura(4) : appliedDet = True
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

            ' Piel del actor PARA ESTE SHAPE (ver el memo de arriba): la máscara sale de sus particiones
            ' BSDismember; 0 (FO4, o shape sin dismember) ⇒ SelectArmatureForShape cae en el primer cubridor
            ' de la región, que es exactamente el comportamiento histórico.
            Dim actorBodySkinTxst As Canon.ITxst = Nothing
            If isOutfitCandidate Then
                Dim shapeSlotMask As UInteger = ShapeBipedSlotMask(shape)
                If Not skinTxstByShapeMask.TryGetValue(shapeSlotMask, actorBodySkinTxst) Then
                    actorBodySkinTxst = ResolveActorSkinTextureSet(state, outfitSkinRegion, shapeSlotMask)
                    skinTxstByShapeMask(shapeSlotMask) = actorBodySkinTxst
                End If
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
                If actorBodySkinTxst.MaterialDe() <> "" Then
                    Dim bgsmMaterial = MaterialResolver.TryLoadMaterialFromDictionary(actorBodySkinTxst.MaterialDe(), material, shape.NifShape, shape.NifContent)
                    If bgsmMaterial IsNot Nothing Then
                        skinMnamLoaded = True
                        If bgsmMaterial.Diffuse_or_Base_Texture <> "" Then material.Diffuse_or_Base_Texture = bgsmMaterial.Diffuse_or_Base_Texture
                        If bgsmMaterial.NormalTexture <> "" Then material.NormalTexture = bgsmMaterial.NormalTexture
                        If bgsmMaterial.SmoothSpecTexture <> "" Then material.SmoothSpecTexture = bgsmMaterial.SmoothSpecTexture
                        If logEnabled Then
                            Dim mnamL = If(actorBodySkinTxst.MaterialDe(), "")
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
