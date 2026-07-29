Imports System.IO
Imports System.Text.Json

''' <summary>
''' NPC_Manager-specific configuration. Persists to its own npc_config.json next to the
''' executable, separate from the shared library config.json (which carries FO4ExePath + the
''' per-game light rigs + skinning/render options that WM also consumes). Settings only ever
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
    ''' <para>⭐ INDEPENDIENTE de <see cref="EmitApplyScript"/>: desde que el apply-script también entrega body
    ''' morphs, las cuatro combinaciones son válidas y el script gana por construcción. Con los dos tildados el
    ''' .ini queda como red para los NPC del plugin que no se re-grabaron (conservan su VMAD viejo, así que el
    ''' script les llega inerte).</para></summary>
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
    ''' shader hace <c>softlight(slot0, slot6) × amplify(slot3)</c> (= el engine).
    ''' True = el render replica lo que el BAKE plegado escribe: pliega
    ''' <c>softlight(complexion, facetint) × amplify(detail)</c> en el slot 0 y NEUTRALIZA slot 3
    ''' (gris 63/64/63 = amplify 1) y slot 6 (gris 0.5 = softlight identidad), de modo que el shader haga la
    ''' identidad y muestre el diffuse plegado.
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

    ''' <summary>BodySlide/OutfitStudio executable chosen in the Edit Body → BodySlide tab (per-game:
    ''' the two games have separate BodySlide installs). NPC_Manager's preflight doesn't ask for it (only
    ''' WM's does — WM_Config.BSExePath), so the tab itself lets the user pick it; the preset combo reads
    ''' the presets from &lt;exe dir&gt;\SliderPresets\*.xml. Flushed to npc_config.json immediately on
    ''' change (same idiom as EmitBodyGenIni).</summary>
    Public Property BodySlideExePath_FO4 As String = ""
    Public Property BodySlideExePath_SSE As String = ""

    ' NOTE: the SELECTED PRESET NAME is deliberately NOT persisted (unlike WM_Config.Default_Preset).
    ' The combo always opens at "(none)": it reflects the CURRENT NPC's state, and restoring a
    ' previously-picked name would show a preset the NPC's sliders don't actually carry (user-reported
    ' lie: NPC saved with custom sliders opened saying "Curvy"). WM can restore it because its combo
    ' drives a stateless preview, not per-NPC data.

    ''' <summary>Size variant (0=Default, 1=Big, 2=Small — BodySlidePresetCatalog.PresetSliderSize) used
    ''' when applying a preset, per-game like the rest. Mirrors WM_Config.Bodytipe. Only meaningful under
    ''' SSE (FO4 presets carry no size variants — the combo is disabled there), but persisted for both so
    ''' the accessor stays symmetric.</summary>
    Public Property BodySlideSize_FO4 As Integer = 0
    Public Property BodySlideSize_SSE As Integer = 0

    ''' <summary>Plugin selection the user confirmed with OK in the preflight, POR JUEGO. Reloaded as the
    ''' pre-ticked set the next time the dialog opens, so a hand-curated selection (e.g. actives minus a
    ''' couple of heavy mods, plus one inactive plugin) survives restarts instead of snapping back to the
    ''' active load order every time.
    ''' <para><b>Separado por juego a propósito</b>: los plugins de FO4 y los de Skyrim no se solapan, así
    ''' que una lista única haría que abrir el otro juego restaurara nombres que no existen en ese Data\ —
    ''' la restauración quedaría vacía y encima pisaría el default de "activos". Mismo criterio que
    ''' <see cref="BodySlideExePath_FO4"/> / <see cref="BodySlideExePath_SSE"/>.</para>
    ''' <para><b>Opt-in explícito</b>: lo gatea el checkbox "Remember this selection for this game" del
    ''' preflight. Vacío (el default de fábrica) ⇒ el diálogo abre en el default de "activos" con el
    ''' checkbox destildado; tildarlo + OK guarda; destildarlo + OK <b>borra</b> el slot de ESE juego (si
    ''' no se borrara, el siguiente open restauraría igual y volvería a tildar el checkbox solo). El slot
    ''' del otro juego nunca se toca desde acá.</para>
    ''' <para>Guardado en el OK del preflight (antes de la carga, así una carga que falla igual conserva la
    ''' selección para poder destildar al culpable). La restauración filtra contra los plugins presentes en
    ''' disco; si no sobrevive ninguno se cae al default de "activos". El botón "Only actives" es la
    ''' válvula de escape para volver al orden de carga del motor sin tocar el checkbox.</para></summary>
    Public Property PreflightSelection_FO4 As New List(Of String)
    Public Property PreflightSelection_SSE As New List(Of String)

    ''' <summary>Selección de preflight guardada para <paramref name="game"/>. Nunca Nothing (lista vacía =
    ''' "nunca se guardó" ⇒ el preflight usa su default de activos).</summary>
    Public Shared Function GetPreflightSelection(game As Config_App.Game_Enum) As List(Of String)
        If Current Is Nothing Then Return New List(Of String)
        Dim saved = If(game = Config_App.Game_Enum.Skyrim, Current.PreflightSelection_SSE, Current.PreflightSelection_FO4)
        Return If(saved, New List(Of String))
    End Function

    ''' <summary>Escribe la selección confirmada en el slot del juego. Copia la lista: el caller
    ''' (Preflight_Form.SelectedPlugins) la reutiliza y limpia en el siguiente OK.</summary>
    Public Shared Sub SetPreflightSelection(game As Config_App.Game_Enum, names As IEnumerable(Of String))
        If Current Is Nothing Then Return
        Dim copy = If(names Is Nothing, New List(Of String), names.ToList())
        If game = Config_App.Game_Enum.Skyrim Then
            Current.PreflightSelection_SSE = copy
        Else
            Current.PreflightSelection_FO4 = copy
        End If
    End Sub

    ''' <summary>"Show:" tree category-filter checkboxes (Section 1 of the NPC tree). Persisted per-app so
    ''' the filter selection survives restarts. Defaults match the WinForms Designer defaults: Unique faces
    ''' on, the rest off. Seeded into the checkboxes on MainForm load; written back in MainForm_FormClosing
    ''' (flushed to npc_config.json by the same SaveConfig() that persists RenderGore).</summary>
    Public Property ShowCatUnique As Boolean = True
    Public Property ShowCatGeneric As Boolean = False
    Public Property ShowCatTemplate As Boolean = False
    Public Property ShowCatUnused As Boolean = False

    ''' <summary>Geometría de la ventana principal, persistida al cerrar y restaurada en MainForm_Load.
    ''' Se guarda el rectángulo NORMAL (RestoreBounds), no el actual: si el usuario cierra maximizado,
    ''' Bounds valdría el monitor entero y al des-maximizar la ventana quedaría pegada al tamaño de pantalla.
    ''' MainWindowWidth = 0 significa "nunca se guardó" → se respeta el default del Designer
    ''' (CenterScreen + Maximized). La restauración valida contra las pantallas ACTUALES: un monitor
    ''' desconectado dejaría la ventana fuera de cualquier escritorio visible.</summary>
    Public Property MainWindowLeft As Integer = 0
    Public Property MainWindowTop As Integer = 0
    Public Property MainWindowWidth As Integer = 0
    Public Property MainWindowHeight As Integer = 0
    Public Property MainWindowMaximized As Boolean = True

    ''' <summary>"Replicate engine skin-weight normalization (non-renormalized)" (CharGen Options -> Fixes).
    ''' <b>Default True</b>, gateado a FO4.
    ''' <para>Replica la normalizacion de pesos de skin que ejecuta el MOTOR: el 4o peso no se lee, se calcula
    ''' <c>w3 = 1 - (w0+w1+w2)</c>, y si sale <c>&lt;= 0</c> se descarta <b>sin renormalizar</b> el resto ⇒ el peso
    ''' efectivo del vertice queda en <c>1+d</c> y la matriz de skin sale escalada. <b>No es un defecto del CK</b>:
    ''' <c>SkinBlend</c> es la misma funcion instruccion por instruccion en <c>CreationKit.exe 0x142B73230</c> y en
    ''' <c>Fallout4.exe 0x141837390</c> ⇒ es el comportamiento del motor, y el blend renormalizado "correcto" no lo
    ''' ejecuta nadie. Detalle y VAs: <see cref="FO4_Base_Library.EngineSkinWeightNormalization"/>.</para>
    ''' <para><b>False = control de regresion</b>, bit-identico al historico. Se mantiene a proposito; no eliminar.</para>
    ''' <para><b>Solo FO4.</b> Verificado por RE unicamente en los binarios de Fallout 4; en Skyrim no esta
    ''' verificado (ni la firma de bytes ni los strings ancla aparecen en SkyrimSE.exe / CreationKit.exe de Skyrim).
    ''' El gate vive en <see cref="ApplyEngineSkinWeightNormalizationGate"/> — nunca escribas
    ''' <c>EngineSkinWeightNormalization.Enabled</c> directo.</para>
    ''' Persistido en npc_config.json.</summary>
    Public Property ReplicateEngineSkinWeightNormalization As Boolean = True

    ''' <summary><b>Gate del camino "FaceGeom en memoria" (head-bake).</b> El preview dibuja la malla PLANA
    ''' y usa el <c>_faceBones</c> sólo como INSUMO (<see cref="HeadBakeService"/> hornea las posiciones y las
    ''' entrega como geometría base vía <c>IBaseGeometryProvider</c>). Es lo que hacen el motor y el CK:
    ''' medido sobre 251 FaceGeom del BA2, el FaceGeom usa el UV del PLANO 227 a 0 y su base material es la del
    ''' plano con el TNAM encima; dibujar el <c>_faceBones</c> hacía además caer el body-weight sobre los 68
    ''' huesos de cara en vez de los ~10 del rig plano (rms 0,1107 / max 2,11 con <c>--headfidelity</c>).
    ''' <para><b>Sólo FO4.</b> El mecanismo <c>_faceBones</c> no existe en Skyrim (no tiene FMRS; RaceMenu usa
    ''' node transforms; 0 archivos <c>*_facebones.nif</c> medidos en una instalación SSE modeada). En SSE
    ''' <c>TryGetFaceBonesVariant</c> devuelve "" y todo esto queda inerte igual, pero se gatea explícito por
    ''' las dudas de un mod que agregue un <c>_faceBones</c> a Skyrim.</para></summary>
    Public Shared Function IsHeadBakeActive() As Boolean
        Return FO4_Base_Library.Config_App.Current IsNot Nothing AndAlso
               FO4_Base_Library.Config_App.Current.Game = FO4_Base_Library.Config_App.Game_Enum.Fallout4
    End Function

    ''' <summary>ÚNICO punto que enciende <see cref="FO4_Base_Library.EngineSkinWeightNormalization.Enabled"/>. Aplica el
    ''' gate por juego: la ley sólo puede activarse para Fallout 4 (ver el ⛔ de
    ''' <see cref="ReplicateEngineSkinWeightNormalization"/>). Llamar tras cargar config, al cambiar de juego y al cambiar
    ''' el checkbox.</summary>
    Public Shared Sub ApplyEngineSkinWeightNormalizationGate(game As FO4_Base_Library.Config_App.Game_Enum)
        FO4_Base_Library.EngineSkinWeightNormalization.Enabled =
            Current.ReplicateEngineSkinWeightNormalization AndAlso game = FO4_Base_Library.Config_App.Game_Enum.Fallout4
    End Sub

    ''' <summary>Ruta del npc_config.json que <see cref="LoadConfig"/>/<see cref="SaveConfig"/> usan.
    ''' PUBLICA a proposito: el CLI headless imprime al arranque de DONDE salio cada opcion que afecta el
    ''' bake (config persistida vs defaults compilados). Sin exponer la ruta ese log seria una afirmacion
    ''' sin fuente — y el StartupPath del CLI NO es el de la GUI, asi que "el config" es ambiguo si no se
    ''' dice cual archivo.</summary>
    Public Shared ReadOnly ConfigFilePath As String = Path.Combine(Application.StartupPath, "npc_config.json")

    Public Shared Sub SaveConfig()
        JsonConfigIO.Save(Current, ConfigFilePath, "NPC_Manager configuration")
    End Sub

    Public Shared Sub LoadConfig()
        Dim cfg = JsonConfigIO.Load(Of NPC_Config)(ConfigFilePath, "NPC_Manager configuration")
        If cfg IsNot Nothing Then Current = cfg
    End Sub
End Class
