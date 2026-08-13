''' <summary>⭐ LAS RUTAS DEL FACEGEN, ESCRITAS UNA VEZ.
''' <para>⛔ Estaban armadas A MANO en 16 sitios repartidos entre <c>FaceGenBuilder</c> (el que ESCRIBE),
''' <c>NpcFaceGenPacker</c> (el que ARCHIVA), <c>NpcFaceTintResolver</c> y <c>FaceTextureRepointer</c> (el
''' que APUNTA el NIF). <c>FaceGenBuilder.ResolveFaceGenPath</c> se declaraba el hogar de la ley y lo
''' esquivaban todos, incluido el propio archivo.</para>
''' <para>El costo de que estuvieran sueltas no es teórico y ya está anotado en <c>FaceTextureRepointer</c>:
''' cambiar la carpeta en el bake y no en el packer ⇒ el NPC se hornea y no entra al archive; no tocar el
''' repointer ⇒ el NIF apunta a un archivo que no existe y la cara sale marrón. Tres sitios que TIENEN que
''' moverse juntos y nada los ataba.</para>
''' <para>⛔ La extensión del NIF va en MAYÚSCULAS (<c>.NIF</c>) porque es lo que escribe el CK, y el
''' resolver de lectura devuelve <c>.nif</c>. En Windows da igual (FS insensible a mayúsculas) pero eran
''' dos convenciones conviviendo sin que ninguna lo dijera; acá quedan las dos, nombradas.</para></summary>
Friend Module FaceGenPaths

    ''' <summary>Los cuatro canales de FaceGenData. El nombre es EL DE LA CARPETA, literal: el motor arma
    ''' la ruta él mismo a partir de estos nombres, así que no son una convención nuestra.</summary>
    Friend Const CanalTint As String = "FaceTint"
    Friend Const CanalDiffuse As String = "FaceDiffuse"
    Friend Const CanalNormal As String = "FaceNormal"
    Friend Const CanalGeom As String = "FaceGeom"
    ''' <summary>⛔ FO4 escribe las texturas de cara (_d/_msn/_s) bajo
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
    ''' <para>⛔ Raíz DISTINTA a <see cref="TexturaDir"/>: FaceCustomization no cuelga de FaceGenData. Eso
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

End Module
