''' <summary>DONDE ESCRIBE ESTA CORRIDA DEL BAKE. Una ley, un archivo, dos consumidores.
''' <para>"" (default) = la carpeta Data del juego, que es lo que hizo siempre. Distinto de "" = el
''' bake escribe su arbol ahi adentro con la MISMA estructura relativa -incluida la carpeta por
''' plugin de <see cref="FaceGenPaths.GeomDir"/>-: no cambia un byte de lo que se escribe, solo de
''' donde cuelga. Lo que se EMBEBE en el NIF es relativo a Data, asi que el archivo horneado es
''' identico en las dos raices.</para>
''' <para>La fija <c>BakeAllRunner</c> desde <c>--outdir</c> ANTES del <c>Parallel.ForEach</c> y la
''' restaura en el mismo <c>Finally</c> que <c>FaceGenBuilder.SkipDdsEncode</c>. Adentro del loop
''' SOLO se lee: escribirla desde el cuerpo del loop seria una carrera entre hilos del ThreadPool.</para>
''' <para>Es de ESCRITURA UNICAMENTE. Todo lo que LEE -el diccionario de archivos, el fallback a
''' disco del comparator- sigue mirando Data, que es donde esta el juego instalado.</para>
''' <para>ESTADO ESTATICO DE PROCESO, y hoy es correcto SOLO POR EXCLUSION: <c>--bake-all</c> nunca
''' levanta <c>MainForm</c> (<c>Program.vb:103-106</c>), asi que el render y Save ESP no pueden verla
''' puesta. El dia que "Bake All" entre al menu de la GUI, ese <c>Finally</c> pasa a ser lo unico que
''' separa esta perilla del resto de la sesion.</para></summary>
Friend Module BakeOutputRoot

    ''' <summary>La raiz elegida para esta corrida. "" = Data. La escribe SOLO <c>BakeAllRunner</c>,
    ''' y ya validada y normalizada (absoluta, sin espacios de guia).</summary>
    Friend Property Elegida As String = ""

    ''' <summary>EL PREDICADO, UNA SOLA VEZ: "?este valor mueve la salida?". Lo llaman los CINCO
    ''' sitios que se hacen la pregunta - la validacion del flag (<c>Program.vb</c>), la validacion de
    ''' forma y el seteo (<c>BakeAllRunner</c>), <see cref="EstaMovida"/> y la copia del clon del
    ''' head-rear (<c>NpcMaterialResolver</c>).
    ''' <para>Toma el valor por parametro justamente para que los que todavia no lo guardaron en
    ''' <see cref="Elegida"/> puedan usar LA MISMA ley. Estuvo escrito de cinco formas distintas y
    ''' dos ya divergian: con <c>--outdir "   "</c>, un guard con <c>= ""</c> y otro con
    ''' <c>IsNullOrWhiteSpace</c> se salteaban LOS DOS y el barrido horneaba sobre Data sin un error
    ''' y sin una linea de log. La tercera vuelta encontro que todavia quedaban CUATRO redacciones
    ''' aunque el defecto ya no estuviera vivo: una ley con cuatro implementaciones es la que vuelve
    ''' a divergir el dia que alguien afloje una.</para></summary>
    Friend Function EsMovida(v As String) As Boolean
        Return Not String.IsNullOrWhiteSpace(If(v, ""))
    End Function

    ''' <summary>?La salida de ESTA corrida esta movida?
    ''' <para>NO se pregunta comparando rutas: comparar la raiz contra
    ''' <c>FilesDictionary_class.FO4Path</c> da falsos positivos porque el <c>FO4_FaceTint_CLI</c> le
    ''' pasa al diccionario su <c>--data</c> verbatim (<c>Program.vb:344</c>/<c>:420</c>), que puede
    ''' ser otra carpeta (<c>:340</c>) o la misma escrita con <c>/</c> en vez de <c>\</c> (<c>:349</c>:
    ''' "Comparar por TEXTO crudo daba un falso positivo SIEMPRE").</para></summary>
    Friend Function EstaMovida() As Boolean
        Return EsMovida(Elegida)
    End Function

    ''' <summary>Donde escribe el bake AHORA.</summary>
    Friend Function Current() As String
        If EstaMovida() Then Return Elegida
        Return Config_App.Current.DataPath
    End Function

End Module
