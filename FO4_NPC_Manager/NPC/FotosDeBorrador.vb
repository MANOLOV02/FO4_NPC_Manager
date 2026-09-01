Imports System.Collections.Concurrent

''' <summary>Las FOTOS inmutables del árbol de los borradores de UNA clase de record: lo que el RENDER
''' recorre mientras el editor sigue trabajando sobre el borrador vivo.
'''
''' <para>⛔ <b>EL PRODUCTOR PUBLICA.</b> Se clona UNA vez, en el hilo que MUTA, y el render lee esa
''' referencia sin recorrer el árbol vivo. Sin esto, <c>CanonHerencia.ArmoEfectivo</c> hace
''' <c>Copia()</c> —un walk RECURSIVO COMPLETO— desde el hilo del render mientras el de UI hace
''' <c>While Armature.Count &gt; 0 : QuitarArmature(0)</c>: enumerar lo que otro hilo modifica tira, y
''' cae en el catch mudo del preview.</para>
'''
''' <para>⛔ <b>NO se le pide la foto al hilo de UI desde el render</b>: eso es un <c>Invoke</c>
''' bloqueante y, si algún camino tiene al de UI esperando al render, es un abrazo mortal en vez de
''' una carrera.</para>
'''
''' <para>⛔ <b>Publicar y retirar son UN PAR, y por eso viven juntos acá.</b> El dueño de la foto es
''' el registro del borrador. Cuando el retiro estaba suelto, la foto le SOBREVIVÍA al borrador —y
''' <see cref="ParaRender"/> la consulta PRIMERO—, así que cancelar, borrar o revertir dejaba el
''' descarte resolviendo para toda la sesión: render, outfit, piel, OBTS y el gate de power armor
''' consultando datos que ya no existen.</para>
'''
''' <para>⛔ <b>GENÉRICA, y por eso hay UNA ley y no tres.</b> Nació typada a ARMO, que era el único
''' que tenía foto; ARMA y MSWP servían el árbol VIVO al hilo del render —<c>ArmaDraftResolver</c> y
''' <c>BuildMswpDataFromDraft</c>— o sea la MISMA carrera, sin la foto. Copiar la clase dos veces
''' habría dejado tres leyes que se separan al primer arreglo; con el parámetro de tipo hay una sola
''' y tres instancias. Las cinco vistas de borrador NO comparten interfaz, así que se publica por
''' <c>(formID, vista)</c> y no por el borrador: no hay tipo común del que colgar
''' <c>.FormID</c>/<c>.Record</c>.</para>
'''
''' <para>⛔ <b>Esto NO es una clase «de la app» ni un caché nuevo</b>: es el mismo diccionario que
''' vivía suelto en <c>MainForm</c>, mudado junto con sus tres operaciones. Estaba suelto y la ley se
''' repartía entre el registro, el desregistro y el remapeo del guardado —y el tercero se la olvidó:
''' mutaba el árbol vivo del borrador SUPERVIVIENTE y no re-publicaba, así que el render seguía
''' sirviendo la foto vieja con la referencia 0xFF ya muerta y la prenda se dibujaba VACÍA hasta el
''' commit siguiente del editor. Con la ley en un solo objeto, el productor que falte no tiene dónde
''' escribir sin pasar por acá.</para></summary>
''' <typeparam name="TVista">La vista canónica del record (<c>Canon.IArmo</c>, <c>Canon.IArma</c>,
''' <c>Canon.IMswp</c>).</typeparam>
Friend NotInheritable Class FotosDeBorrador(Of TVista As Class)

    ''' <summary>La foto por FormID de borrador. Concurrente porque la ESCRIBE el hilo de UI (el commit
    ''' del editor, el remapeo del guardado) y la LEE el del render, sin candado entre medio.</summary>
    Private ReadOnly _fotos As New ConcurrentDictionary(Of UInteger, TVista)

    ''' <summary>Cómo se llega al borrador VIVO cuando no hay foto. Se fija UNA vez, en la construcción,
    ''' para que <see cref="ParaRender"/> quede con la MISMA forma que el resolvedor del render
    ''' (<c>Func(Of UInteger, TVista)</c>) y se pueda cablear directo, sin envoltorio que pueda diferir
    ''' entre producción y el testigo.</summary>
    Private ReadOnly _vivo As Func(Of UInteger, TVista)

    ''' <param name="vivo">El borrador vivo por FormID (en producción, <c>TryGetXxxDraft(f)?.Record</c>).</param>
    Friend Sub New(vivo As Func(Of UInteger, TVista))
        _vivo = vivo
    End Sub

    ''' <summary>Publica la foto: la CLONA acá, en el hilo que la pide. El llamador pasa el record VIVO
    ''' y la copia la hace ESTA función — si el clon viviera afuera, cada productor tendría su versión
    ''' de la ley y el que se equivocara publicaría una referencia al árbol que sigue mutando.
    ''' <para>Cuando <c>Copia()</c> devuelve Nothing —la vista no es un <c>CanonRecordView</c>, su árbol
    ''' o su contexto son nulos, o el <c>TryCast</c> interno falla porque <c>Reenvolver</c> desempató por
    ''' la FIRMA— se RETIRA la que hubiera. Dejar la anterior sería servir una foto de un contenido que
    ''' ya no es el del borrador.</para></summary>
    Friend Sub Publicar(formID As UInteger, vista As TVista)
        If formID = 0UI OrElse vista Is Nothing Then Return
        Dim copia As TVista = Canon.CanonInterpretacion.Copia(vista)
        If copia IsNot Nothing Then
            _fotos(formID) = copia
        Else
            _fotos.TryRemove(formID, Nothing)
        End If
    End Sub

    ''' <summary>Retira la foto. Va con el borrador: cuando se lo desregistra (cancelar / borrar /
    ''' revertir) y cuando se PROMUEVE a record real (ya lo enumera el orden de carga).</summary>
    Friend Sub Retirar(formID As UInteger)
        _fotos.TryRemove(formID, Nothing)
    End Sub

    ''' <summary>La vista que el RENDER puede recorrer sin carrera: la foto si existe, y el borrador
    ''' vivo si no (un borrador registrado antes de que existiera la foto).</summary>
    Friend Function ParaRender(formID As UInteger) As TVista
        If formID = 0UI Then Return Nothing
        Dim foto As TVista = Nothing
        If _fotos.TryGetValue(formID, foto) AndAlso foto IsNot Nothing Then Return foto
        Return _vivo?.Invoke(formID)
    End Function

End Class
