''' <summary>LAS RUTAS DEL FACEGEN, ESCRITAS UNA VEZ.
''' <para>Estaban armadas A MANO en 16 sitios repartidos entre <c>FaceGenBuilder</c> (el que ESCRIBE),
''' <c>NpcFaceGenPacker</c> (el que ARCHIVA), <c>NpcFaceTintResolver</c> y <c>FaceTextureRepointer</c> (el
''' que APUNTA el NIF). <c>FaceGenBuilder.ResolveFaceGenPath</c> se declaraba el hogar de la ley y lo
''' esquivaban todos, incluido el propio archivo.</para>
''' <para>El costo de que estuvieran sueltas no es teórico y ya está anotado en <c>FaceTextureRepointer</c>:
''' cambiar la carpeta en el bake y no en el packer ⇒ el NPC se hornea y no entra al archive; no tocar el
''' repointer ⇒ el NIF apunta a un archivo que no existe y la cara sale marrón. Tres sitios que TIENEN que
''' moverse juntos y nada los ataba.</para>
''' <para>La extensión del NIF va en MAYÚSCULAS (<c>.NIF</c>) porque es lo que escribe el CK, y el
''' resolver de lectura devuelve <c>.nif</c>. En Windows da igual (FS insensible a mayúsculas) pero eran
''' dos convenciones conviviendo sin que ninguna lo dijera; acá quedan las dos, nombradas.</para></summary>
Friend Module FaceGenPaths

    ''' <summary>Los cuatro canales de FaceGenData. El nombre es EL DE LA CARPETA, literal: el motor arma
    ''' la ruta él mismo a partir de estos nombres, así que no son una convención nuestra.</summary>
    Friend Const CanalTint As String = "FaceTint"
    Friend Const CanalDiffuse As String = "FaceDiffuse"
    Friend Const CanalNormal As String = "FaceNormal"
    Friend Const CanalGeom As String = "FaceGeom"
    ''' <summary>FO4 escribe las texturas de cara (_d/_msn/_s) bajo
    ''' <c>Textures\Actors\Character\FaceCustomization\</c>, que NO cuelga de FaceGenData — ese es el
    ''' árbol de SSE. Son dos raíces distintas, y por eso este canal necesita su propia función.</summary>
    Friend Const CanalCustomization As String = "FaceCustomization"

    Private Const RaizMallas As String = "Meshes\Actors\Character\FaceGenData\"
    Private Const RaizTexturas As String = "Textures\Actors\Character\FaceGenData\"

    ''' <summary>Carpeta relativa del FaceGeom de un plugin, con la barra final.
    ''' <c>Meshes\Actors\Character\FaceGenData\FaceGeom\&lt;plugin&gt;\</c></summary>
    Friend Function GeomDir(originPlugin As String) As String
        Return RaizMallas & CanalGeom & "\" & originPlugin & "\"
    End Function

    ''' <summary>Ruta relativa del NIF de FaceGeom, en MINÚSCULAS (<c>.nif</c>) — la forma con la que se
    ''' BUSCA (el diccionario de archivos y el packer).</summary>
    Friend Function GeomNif(originPlugin As String, formIdLocal As UInteger) As String
        Return GeomDir(originPlugin) & formIdLocal.ToString("X8") & ".nif"
    End Function

    ''' <summary>Nombre del archivo NIF tal como lo ESCRIBE el CK: extensión en MAYÚSCULAS, más el sufijo
    ''' de sandbox cuando corresponde (<c>_2</c> del modo debug, <c>_2c</c> del replacer forzado de SSE).</summary>
    Friend Function GeomNifFileName(formIdLocal As UInteger, Optional sufijo As String = "") As String
        Return formIdLocal.ToString("X8") & sufijo & ".NIF"
    End Function

    ''' <summary>Carpeta relativa de un canal de TEXTURA, con la barra final.
    ''' <c>Textures\Actors\Character\FaceGenData\&lt;canal&gt;\&lt;plugin&gt;\</c></summary>
    Friend Function TexturaDir(canal As String, originPlugin As String) As String
        Return RaizTexturas & canal & "\" & originPlugin & "\"
    End Function

    ''' <summary>Carpeta relativa de las texturas de cara de FO4, con la barra final.
    ''' <c>Textures\Actors\Character\FaceCustomization\&lt;plugin&gt;\</c>
    ''' <para>Raíz DISTINTA a <see cref="TexturaDir"/>: FaceCustomization no cuelga de FaceGenData. Eso
    ''' fue lo que me hizo buscar las texturas horneadas en la carpeta equivocada y concluir que el bake no
    ''' escribía ninguna.</para></summary>
    Friend Function CustomizacionDir(originPlugin As String) As String
        Return "Textures\Actors\Character\" & CanalCustomization & "\" & originPlugin & "\"
    End Function

    ''' <summary>Ruta relativa de un DDS de un canal. <paramref name="sufijo"/> va ANTES de la extensión
    ''' (<c>_2</c>, <c>_2b</c>, <c>_2c</c>, <c>_2d</c>): es como se nombran los sandboxes.</summary>
    Friend Function TexturaDds(canal As String, originPlugin As String, formIdLocal As UInteger,
                               Optional sufijo As String = "") As String
        Return TexturaDir(canal, originPlugin) & formIdLocal.ToString("X8") & sufijo & ".dds"
    End Function

    ''' <summary>Las salidas de TEXTURA de un bake de cara, como IDENTIDAD y no como ruta. Existe para que
    ''' el bake pueda DECLARAR cuáles le correspondía producir y el packer exija exactamente ésas, sin que
    ''' ninguno de los dos arme un string para compararlo con el del otro.
    ''' <para>⛔ <c>Ninguna</c> NO significa "no aplica": significa <b>se exige SIEMPRE y sin chequeo de
    ''' pertenencia</b>. Hoy lo lleva SOLO el NIF de FaceGeom, que el bake escribe siempre que llega a
    ''' Success, asi que su presencia en disco ya es prueba de que es de este horneado.</para>
    ''' <para>El facetint de SSE llevaba <c>Ninguna</c> y estaba MAL: eso son DOS cosas -"requerido
    ''' siempre" y "exento de pertenencia"- y solo queriamos la primera. Con la segunda, un facetint de un
    ''' horneado ANTERIOR entraba al BSA junto al NIF nuevo por el solo hecho de existir (el barrido de SSE
    ''' no toca FaceTint\ a proposito), y la cara salia mezcla de dos horneados. Ahora lleva su tag y el
    ''' bake lo DECLARA al entrar a WriteSseFacetintDds: sigue exigido siempre -si falta, el motor deja el
    ''' tint en NULL y la cara sale marron- pero ademas tiene que ser SUYO.</para></summary>
    <Flags>
    Friend Enum SalidaDeTexturaDeCara
        Ninguna = 0
        Fo4Diffuse = 1
        Fo4Normal = 2
        Fo4Specular = 4
        SseFaceTint = 8
        SseHeadDiffuse = 16
        SseHeadNormal = 32
    End Enum

    ''' <summary>Una salida de textura de cara de FO4: qué slot del texture-set ocupa, con qué sufijo se
    ''' nombra su archivo, y qué identidad tiene.</summary>
    Friend Structure SalidaFo4
        Public ReadOnly Slot As Integer
        Public ReadOnly SufijoCanon As String
        Public ReadOnly Salida As SalidaDeTexturaDeCara

        Public Sub New(slot As Integer, sufijoCanon As String, salida As SalidaDeTexturaDeCara)
            Me.Slot = slot
            Me.SufijoCanon = sufijoCanon
            Me.Salida = salida
        End Sub
    End Structure

    ''' <summary>LA tabla de salidas de textura de cara de FO4, en el orden en que el bake las compone y el
    ''' packer las lista. El plan de slots del bake y la lista de archivos del packer se construyen
    ''' RECORRIENDO ESTA TABLA: antes el conjunto {slot, sufijo} estaba escrito dos veces, una en cada lado,
    ''' y un canal nuevo obligaba a acordarse de los dos. Acá un canal nuevo es UNA FILA.</summary>
    Friend ReadOnly SalidasFo4 As SalidaFo4() = {
        New SalidaFo4(0, "_d.dds", SalidaDeTexturaDeCara.Fo4Diffuse),
        New SalidaFo4(1, "_msn.dds", SalidaDeTexturaDeCara.Fo4Normal),
        New SalidaFo4(7, "_s.dds", SalidaDeTexturaDeCara.Fo4Specular)
    }

    ''' <summary>El sufijo de archivo de una salida de cara: <c>_d.dds</c>, o <c>_d_2.dds</c> con el
    ''' sandbox del modo debug. El <c>_2</c> va ANTES de la extensión, igual que en el resto del módulo.
    ''' Lo consumen el plan de slots del bake y la lista de specs del packer, que es exactamente el par que
    ''' antes armaba cada uno su propia versión.</summary>
    Friend Function SufijoDe(salida As SalidaFo4, sandbox As Boolean) As String
        Return If(sandbox, salida.SufijoCanon.Replace(".dds", "_2.dds"), salida.SufijoCanon)
    End Function

    ''' <summary>Nombre del archivo de una salida de cara de FO4: <c>&lt;id&gt;_d.dds</c>, o
    ''' <c>&lt;id&gt;_d_2.dds</c> en modo debug.</summary>
    Friend Function CustomizacionDdsFileName(formIdLocal As UInteger, salida As SalidaFo4,
                                             sandbox As Boolean) As String
        Return formIdLocal.ToString("X8") & SufijoDe(salida, sandbox)
    End Function

    ''' <summary>Ruta relativa a Data de una salida de cara de FO4.
    ''' <c>Textures\Actors\Character\FaceCustomization\&lt;plugin&gt;\&lt;id&gt;_d.dds</c></summary>
    Friend Function CustomizacionDds(originPlugin As String, formIdLocal As UInteger, salida As SalidaFo4,
                                     sandbox As Boolean) As String
        Return CustomizacionDir(originPlugin) & CustomizacionDdsFileName(formIdLocal, salida, sandbox)
    End Function

End Module
