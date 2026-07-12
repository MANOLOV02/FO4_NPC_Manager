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

    ''' <summary>Archive target for the baked CharGen under Skyrim (SSE, per-app setting). SSE has no BA2
    ''' header-version choice — it either packs a BSA (v105) or leaves the bake outputs loose. Values:
    ''' 0 = Loose (skip BSA pack), 1 = BSA (default). Serializes automatically via JSON (see SaveConfig /
    ''' LoadConfig); old configs without the key default to 1 = BSA. The game-aware loose decision routes
    ''' through <see cref="IsLooseOnly"/>; the FO4 side keeps reading <see cref="Ba2Version_FO4"/>.</summary>
    Public Property Archive_SSE As UInteger = 1

    ''' <summary>Game-aware "leave the CharGen bake outputs loose (skip archive pack)" decision. For FO4 this
    ''' is exactly the historical <c>Ba2Version_FO4 = 0</c> sentinel (byte-identical behavior); for SSE it is
    ''' <c>Archive_SSE = 0</c>. Null-guards <see cref="Current"/> (returns True — stays loose — when config is
    ''' unavailable, matching the prior FaceGenBuilder.OutputStaysLoose guard).</summary>
    Public Shared Function IsLooseOnly(game As Config_App.Game_Enum) As Boolean
        If Current Is Nothing Then Return True
        Return If(game = Config_App.Game_Enum.Skyrim, Current.Archive_SSE = 0, Current.Ba2Version_FO4 = 0)
    End Function

    ''' <summary>"Remove 'Is CharGen Face Preset' flag" option in the Save ESP dialog (sub-option of the
    ''' CharGen bake). When True (default) the saved NPC overrides get ACBS bit 0x04 cleared so the engine
    ''' loads the baked FaceGen instead of reconstructing the face at runtime. Persisted per-app: the dialog
    ''' seeds the checkbox from this and writes it back on toggle (flushed to npc_config.json on app close,
    ''' same as Ba2Version_FO4).</summary>
    Public Property RemoveCharGenFlagOnBake As Boolean = True

    ''' <summary>"Emit BodyGen .ini" option in the Save ESP dialog. When True (default) the save writes the
    ''' BodyGen <c>templates.ini</c> + <c>morphs.ini</c> pair so the engine applies the BodySlide sliders to
    ''' the NPC on its first in-game load. Game-aware output dir: FO4 →
    ''' <c>Data\F4SE\Plugins\F4EE\BodyGen\&lt;plugin&gt;\</c> (LooksMenu); SSE →
    ''' <c>Data\Meshes\actors\character\BodyGenData\&lt;plugin&gt;\</c> (RaceMenu).
    ''' <para>Default flipped to True: BodyGen is the ONLY delivery route for body morphs — without it the
    ''' sliders exist in our sidecar and nowhere the game can see. Persisted per-app; the dialog seeds the
    ''' checkbox from this and writes it back on toggle (flushed to npc_config.json on app close).</para></summary>
    Public Property EmitBodyGenIni As Boolean = True

    ''' <summary>"Emit apply-script" option in the Save ESP dialog. When True (default) the save attaches our
    ''' Papyrus script to the NPC_ record via VMAD (NpcVmadBuilder) and installs the compiled .pex, so the
    ''' engine applies — on the actor's first spawn — the RaceMenu/LooksMenu options that have NO other
    ''' delivery route: body/hands/feet overlays, skin overrides, and (SSE only) node transforms.
    ''' <para>NOT covered by the script, because they already ship another way: body morphs (BodyGen .ini)
    ''' and everything face-related (baked into the FaceGen NIF/textures).</para>
    ''' <para>Soft dependency: the plugin carries no master on RaceMenu/LooksMenu. Without SKSE/F4SE the
    ''' native class is simply not registered and the call no-ops — the record and the baked FaceGen still
    ''' work. Persisted per-app, same mechanism as <see cref="EmitBodyGenIni"/>.</para></summary>
    Public Property EmitApplyScript As Boolean = True

    ''' <summary>"Apply fix to ghoul headrear" toggle (CharGen Options UI). When True, the ghoul-female
    ''' head-rear nape is given the vanilla-UV body texture via a disk clone (see MainForm IsGhoulHeadRearCase /
    ''' ApplyGhoulHeadRearClonedTextures). Default False = opt-in; the fix does nothing unless enabled.
    ''' Persisted to npc_config.json (flushed on app close, same as RenderGore).</summary>
    Public Property ApplyGhoulHeadRearFix As Boolean = False

    ''' <summary>⚠️ PROVISORIO (herramienta de diagnóstico, a ELIMINAR) — "SSE: render por el camino PLEGADO".
    ''' False (default) = el render SSE normal: slot 0 = complexion, slot 3 = detail, slot 6 = facetint compuesto, y el
    ''' shader hace <c>fgTint(slot6) × softlight(slot0, slot3)</c> (= el engine).
    ''' True = el render replica lo que el BAKE plegado escribe: pliega <c>fgTint × softlight(complexion, detail)</c>
    ''' en el slot 0 y NEUTRALIZA slot 3 (gris 0.5 = softlight identidad) y slot 6 (gris 63/64/63 = fgTint 1), de modo
    ''' que el shader haga la identidad y muestre el diffuse plegado.
    ''' Si el pliegue es correcto, AMBOS caminos deben dar el MISMO tono de piel. Sirve para verlo in-app sin bakear.
    ''' NO se persiste (arranca siempre en False): es un toggle de sesión para diagnóstico.
    ''' ⛔ &lt;JsonIgnore&gt; DE VERDAD: sin él SÍ se persistía (el comentario mentía — apareció en npc_config.json), y un
    ''' toggle de diagnóstico que sobrevive al reinicio deja la app en un modo raro sin que nadie sepa por qué.</summary>
    <Serialization.JsonIgnore>
    Public Property SseRenderFoldedPath As Boolean = False

    ''' <summary>SANDBOX de paridad del pliegue SSE: cuando el stack de capas (skee MASKT + overlays de cara) se compone
    ''' por GPU, correr TAMBIÉN el CPU y loguear el RMS entre los dos (<c>[SSE-FOLD] ... rmsCPUvsGPU=</c>). Es la MEDIDA
    ''' de la paridad — sin esto la paridad sería una afirmación, no un dato.
    ''' ⚠️ OPT-IN (False por defecto, también en Debug): DUPLICA el compose, y el camino CPU pasa la cara entera por
    ''' Double + decodifica las texturas de cada capa ⇒ MEDIDO +3,6 s por render a 1024² (log 2026-07-12). Se enciende
    ''' a mano cuando se quiere re-medir (p.ej. al tocar el compositor o agregar un blend-op nuevo).
    ''' ⛔ &lt;JsonIgnore&gt;: NO se persiste. Se persistía, y ese <c>true</c> guardado pisaba el default ⇒ el compose corría
    ''' DUPLICADO en cada render para siempre (era la lentitud que reportó el usuario). Un flag de diagnóstico caro no
    ''' puede sobrevivir al reinicio: se enciende a mano, para la corrida en la que se quiere medir.
    ''' Mediciones vigentes (2026-07-12, 1024², bake de 0008774F): overlay de cara → rmsCPUvsGPU = 0,080/255;
    ''' skee MASKT (rama PaletteMask, canal R) → 0,001/255. Ambas bajo el redondeo al byte = float32 vs double.</summary>
    <Serialization.JsonIgnore>
    Public Property SseMeasureFoldParity As Boolean = False

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
