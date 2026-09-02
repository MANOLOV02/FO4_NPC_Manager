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
''' y CINCO instancias —ARMO, ARMA, MSWP, OTFT y LVLI—. Las cinco vistas NO comparten interfaz, así que se publica por
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
''' <typeparam name="TVista">Lo que se fotografía: la vista canónica del record —<c>Canon.IArmo</c>,
''' <c>Canon.IArma</c>, <c>Canon.IMswp</c>, <c>Canon.ILvli</c>— o el BORRADOR entero
''' (<c>OutfitDraft</c>), que lleva estado fuera del record y por eso pasa su propio clonador.</typeparam>
Friend NotInheritable Class FotosDeBorrador(Of TVista As Class)

    ''' <summary>La foto por FormID de borrador. Concurrente porque la ESCRIBE el hilo de UI (el commit
    ''' del editor, el remapeo del guardado) y la LEE el del render, sin candado entre medio.</summary>
    Private ReadOnly _fotos As New ConcurrentDictionary(Of UInteger, TVista)

    ''' <summary>Cómo se clona la vista. Entra por constructor porque NO todas las clases de borrador se
    ''' clonan igual: ARMO/ARMA/MSWP <b>son</b> su record y se copian con el walk canónico; el borrador de
    ''' ATUENDO lleva estado FUERA del record —las realizaciones selladas— así que copiar sólo el record
    ''' lo fotografía por la mitad y la carrera se MUDA en vez de cerrarse. Con la estrategia por
    ''' parámetro hay UNA ley y una clase; lo que cambia es cómo se saca la copia.</summary>
    Private ReadOnly _clonar As Func(Of TVista, TVista)

    ''' <param name="clonar">Cómo sacar la copia. Omitido = el walk canónico <c>Copia</c>, que es lo que
    ''' sirve para las CUATRO clases que SON su record (ARMO, ARMA, MSWP, LVLI); el de atuendo pasa el suyo porque lleva
    ''' las realizaciones fuera del record.</param>
    Friend Sub New(Optional clonar As Func(Of TVista, TVista) = Nothing)
        _clonar = clonar
    End Sub

    ''' <summary>Publica la foto: la CLONA acá, en el hilo que la pide. El llamador pasa el record VIVO
    ''' y la copia la hace ESTA función — si el clon viviera afuera, cada productor tendría su versión
    ''' de la ley y el que se equivocara publicaría una referencia al árbol que sigue mutando.
    '''
    ''' <para>⛔ UN CLON EN <c>Nothing</c> ES UN BUG Y SE TIRA — antes se RETIRABA la foto. El cambio es lo
    ''' que sostiene que el fondo pueda leer la foto Y NADA MÁS: si publicar pudiera dejar al borrador sin
    ''' foto, «registrado ⇒ tiene foto» dejaría de ser un invariante y el lector foto-sólo devolvería
    ''' Nothing sobre un borrador que existe. MEDIDO antes de cambiarlo: ninguno de los cinco clonadores
    ''' puede devolver Nothing legítimamente. <c>OutfitDraft.Clone</c> TIRA (exige el record); el walk
    ''' canónico devuelve Nothing sólo si la vista no es <c>CanonRecordView</c>, o su <c>Node</c> o su
    ''' <c>Context</c> son nulos, o <c>Reenvolver</c> desempata a otra clase por la FIRMA
    ''' (<c>CanonInterpretacion:1257-1266</c>) — y todo borrador nace por una puerta que ya corrió
    ''' <c>Copia()</c> bajo <c>Borradores.ExigirRecord</c>, que TIRA en esos mismos casos. O sea que la
    ''' rama Nothing era código muerto que sólo servía para romper el invariante si alguna vez se
    ''' alcanzaba.</para>
    ''' <para>El throw se PROPAGA: es un bug de datos, no un estado que esta clase pueda decidir por su
    ''' cuenta. Quien llame desde un camino que no puede morir —el guardado— tiene que ordenar sus pasos
    ''' para que un throw no lo deje a medias (ver <c>Borradores.RemapearSupervivientes</c>), y quien
    ''' llame desde un manejador de UI tiene que envolverlo (ver
    ''' <c>OutfitPicker_Form.RequestPreviewAsync</c>).</para></summary>
    Friend Sub Publicar(formID As UInteger, vista As TVista)
        If formID = 0UI OrElse vista Is Nothing Then Return
        ' Sin clonador explicito: el walk canonico, que es la conducta historica y byte a byte.
        Dim copia As TVista = If(_clonar Is Nothing, Canon.CanonInterpretacion.Copia(vista), _clonar(vista))
        If copia Is Nothing Then
            Throw New InvalidOperationException(
                $"No se pudo fotografiar el borrador {formID:X8} ({GetType(TVista).Name}): el clonador " &
                "devolvio Nothing. Todo borrador registrado tiene foto —de eso depende que el hilo de " &
                "fondo pueda leer SOLO la foto—, asi que esto es un bug del clonador o del record, no un " &
                "estado posible.")
        End If
        _fotos(formID) = copia
    End Sub

    ''' <summary>Retira la foto. Va con el borrador: cuando se lo desregistra (cancelar / borrar /
    ''' revertir) y cuando se PROMUEVE a record real (ya lo enumera el orden de carga).</summary>
    Friend Sub Retirar(formID As UInteger)
        _fotos.TryRemove(formID, Nothing)
    End Sub

    ''' <summary>¿HAY foto de este FormID? La PRESENCIA, sin traer la vista.
    ''' <para>Existe aparte de <see cref="ParaRender"/> porque la pregunta es otra —«¿es un borrador de
    ''' esta clase?» y no «dame su vista»—, y así el llamador no tiene que comparar contra Nothing. Desde
    ''' que <c>ParaRender</c> es foto-sólo las dos contestan lo mismo; cuando ésta nació, <c>ParaRender</c>
    ''' todavía caía al árbol VIVO y usarla de predicado recorría el registro justo en el caso más común
    ''' —el FormID que NO es borrador—, que era la carrera entera.</para>
    ''' <para>Y contesta «es un borrador de esta clase» porque publicar/retirar son UN PAR: toda puerta
    ''' que registra publica y toda puerta que baja retira.</para></summary>
    Friend Function TieneFoto(formID As UInteger) As Boolean
        If formID = 0UI Then Return False
        Return _fotos.ContainsKey(formID)
    End Function

    ''' <summary>La vista que el RENDER recorre: <b>LA FOTO, Y NADA MÁS</b>. Nothing = no es un borrador
    ''' de esta clase.
    '''
    ''' <para>⛔ EL FALLBACK AL ÁRBOL VIVO SE ELIMINÓ, y era el último resto de la carrera que esta clase
    ''' vino a cerrar. Servía «la foto si existe, y el borrador vivo si no», y ese «si no» salía a recorrer
    ''' la <c>List</c> de borradores desde el hilo de FONDO —<c>BuildOutfitComboEntries</c> corre adentro
    ''' de <c>Task.Run</c>— mientras el de UI registraba, restauraba o borraba: una <c>List</c> que muta
    ''' bajo un <c>For Each</c> tira <c>InvalidOperationException</c>. Y el caso más común es justamente el
    ''' peor: un FormID que NO es borrador recorre la lista ENTERA para contestar Nothing.</para>
    '''
    ''' <para>Se puede eliminar POR CONSTRUCCIÓN, no por optimismo: el registro publica la foto ANTES de
    ''' tocar la lista (<c>MainForm.RegistrarConFoto</c>) y <see cref="Publicar"/> TIRA si el clon sale en
    ''' Nothing, así que «registrado ⇒ tiene foto» vale siempre y el fallback no cubría ningún caso real —
    ''' sólo aportaba la carrera.</para>
    ''' <para>La lista viva NO desaparece: es el ORDEN del guardado, y vive en el hilo de UI. El saver y
    ''' los editores la siguen enumerando desde ahí, que es donde se puede.</para></summary>
    Friend Function ParaRender(formID As UInteger) As TVista
        If formID = 0UI Then Return Nothing
        Dim foto As TVista = Nothing
        If _fotos.TryGetValue(formID, foto) Then Return foto
        Return Nothing
    End Function

End Class
