Imports FO4_Base_Library
Imports FO4_Base_Library.Canon.CanonInterpretacion

''' <summary>Una armadura que se está editando y todavía no se guardó.
'''
''' <para>El borrador NO copia el record: LO ES. <see cref="Record"/> es el árbol de campos, y
''' editarlo es editar lo que se va a guardar. Antes esta clase repetía cada campo del record y
''' había que acordarse de volcarlos de un lado al otro al abrir el editor y al guardar; el campo
''' que alguien se olvidaba de copiar se perdía sin ruido.</para>
'''
''' <para>Lo único que agrega son los datos de AUTORÍA, que no viven en el record: si es nuevo o una
''' edición de uno existente, y si tiene cambios sin guardar.</para>
'''
''' <para>Dos formas:</para>
''' <list type="bullet">
''' <item><b>Nuevo</b>: el identificador es provisional (byte alto 0xFF) para que un NPC o un atuendo
''' puedan referenciarlo antes de guardar; al guardar se le asigna el real y se reindexa.</item>
''' <item><b>Edición</b>: el identificador ES el real del record que se está sobrescribiendo.</item>
''' </list>
'''
''' <para>Ya no hace falta una marca aparte de "se tocaron las combinaciones": comparar por los
''' bytes que produce el record detecta cualquier cambio, y además lo detecta en las dos
''' direcciones, así que deshacer una edición vuelve a dejar el borrador limpio.</para></summary>
Public Class ArmoDraft

    ''' <summary>Prefijo del identificador de editor. Al guardar se le inyecta el nombre del archivo
    ''' destino, para que sea reconocible y no choque entre plugins.</summary>
    Public Const EditorIdPrefix As String = "npcm_ARMO_"

    ''' <summary>El record que se está editando. Todo lo que el usuario cambia va acá.</summary>
    Public Property Record As Canon.IArmo

    ''' <summary>Nuevo: identificador provisional. Edición: el real del record original.</summary>
    Public Property FormID As UInteger

    ''' <summary>True = edita un record existente. False = uno nuevo.</summary>
    Public Property IsOverride As Boolean

    ''' <summary>Todavía no se escribió nunca.</summary>
    Public Property IsNew As Boolean = True

    ''' <summary>Ya se escribió antes y se volvió a editar.</summary>
    Public Property IsModified As Boolean = False

    ''' <summary>Cualquiera de las dos obliga a (re)escribirla al guardar.</summary>
    Public ReadOnly Property IsDirty As Boolean
        Get
            Return IsNew OrElse IsModified
        End Get
    End Property

    '==============================================================================================
    ' Creación
    '==============================================================================================

    ''' <summary>Una armadura nueva, vacía.</summary>
    Public Shared Function Nuevo(formID As UInteger, game As Canon.WbGame) As ArmoDraft
        Dim r = Canon.CanonRecords.ArmoNuevo(game)
        Borradores.ExigirRecord(r, "ARMO", $"el formato de {game} no declara ese record")
        Return New ArmoDraft With {.Record = r,
                                   .FormID = formID, .IsOverride = False, .IsNew = True}
    End Function

    ''' <summary>Una edición de un record que ya existe. Se trabaja sobre una COPIA: cancelar el
    ''' editor tiene que dejar el original como estaba.</summary>
    Public Shared Function Edicion(rec As PluginRecord, plugins As PluginManager) As ArmoDraft
        Borradores.ExigirPluginsNormalizados(plugins)
        If rec Is Nothing Then Return Nothing
        Dim abierto = Canon.CanonRecords.Armo(rec, plugins)
        If abierto Is Nothing Then Return Nothing
        Dim copia = abierto.Copia()
        Borradores.ExigirRecord(copia, "ARMO", "la copia del record falló: árbol o contexto nulos, o la firma no corresponde a esta vista")
        Return New ArmoDraft With {.Record = copia, .FormID = rec.Header.FormID,
                                   .IsOverride = True, .IsNew = False}
    End Function

    ''' <summary>Un record NUEVO a partir de uno que ya existe (una plantilla). Igual que
    ''' <see cref="Edicion"/> —se trabaja sobre una COPIA con contexto propio— pero con identidad nueva.
    ''' <para>⛔ No se reconstruye campo por campo. Copiar el árbol trae TODO lo que el record tenía,
    ''' incluidos los campos que la app no modela y los que ningún editor muestra; enumerarlos a mano
    ''' garantiza que alguno falte, y el que falta no se nota hasta que alguien lo busca en el archivo.
    ''' En Skyrim, además, la construcción a mano normalizaba la plantilla de cuerpo de BODT a BOD2 y
    ''' perdía General Flags y Armor Type — el 85 % de los ARMA del juego.</para>
    ''' <para>El EditorID NO se toca acá: lo pone el editor, que es quien sabe si el usuario le dio uno
    ''' o hay que sintetizarlo.</para></summary>
    Public Shared Function Clon(rec As PluginRecord, plugins As PluginManager,
                                formIDNuevo As UInteger) As ArmoDraft
        Dim d = Edicion(rec, plugins)
        If d Is Nothing Then Return Nothing
        Return ClonDesdeCopia(d.Record, formIDNuevo)
    End Function

    ''' <summary>La cola COMÚN de todo clon: exigir la copia y darle identidad nueva.
    ''' <para>⛔ Existe para que <see cref="Clon"/> (que copia desde el DISCO) y
    ''' <see cref="ClonDeBorrador"/> (que copia desde un borrador PROPIO) no puedan divergir en la
    ''' identidad — que es exactamente donde ya se cazó un defecto una vez: un clon que nacía
    ''' <c>Deleted</c> porque heredaba <c>RecordFlags</c> de su fuente. La identidad del clon la pone
    ''' <see cref="Borradores.ReidentificarComoClon"/>, no cada borrador.</para></summary>
    Private Shared Function ClonDesdeCopia(copia As Canon.IArmo, formIDNuevo As UInteger) As ArmoDraft
        Borradores.ExigirRecord(copia, "ARMO", "la copia del record falló: árbol o contexto nulos, o la firma no corresponde a esta vista")
        Dim d As New ArmoDraft With {.Record = copia, .FormID = formIDNuevo,
                                     .IsOverride = False, .IsNew = True}
        Borradores.ReidentificarComoClon(d.Record, formIDNuevo)
        Return d
    End Function

    ''' <summary>Un record NUEVO a partir de un BORRADOR PROPIO que el usuario ya está editando.
    ''' <para>⛔ Existe porque «copiar» tiene que copiar LO QUE EL USUARIO VE. <see cref="Clon"/> parte de
    ''' un <c>PluginRecord</c>, o sea del ARCHIVO: clonar desde ahí una armadura que el usuario ya editó
    ''' le devolvía la versión vanilla, sin los addons que acababa de agregar. Decisión del usuario
    ''' (2026-09-05), no derivada.</para>
    ''' <para>⛔ Y es una GEMELA, no un cambio de firma de <see cref="Clon"/>. Hacer que <c>Clon</c> tome
    ''' la vista sacaría del camino la guarda <see cref="Borradores.ExigirPluginsNormalizados"/> que hoy
    ''' le impone <see cref="Edicion"/>: <c>Clon(CanonRecords.Armo(rec, Nothing), …)</c> compilaría y el
    ''' clon nacería con los FormID LOCALES del archivo fuente, apuntando al mod equivocado sin un aviso.
    ''' Un <c>ArmoDraft</c>, en cambio, ya está normalizado por construcción —lo garantizan sus cuatro
    ''' puertas—, así que acá la garantía la lleva el TIPO y no una guarda que se pueda saltear.</para></summary>
    Public Shared Function ClonDeBorrador(origen As ArmoDraft, formIDNuevo As UInteger) As ArmoDraft
        ' ⛔ TIRA, no devuelve Nothing: pasar un borrador sin record es error de LLAMADOR, no un dato
        ' posible, y con un `Return Nothing` el editor abriría EN BLANCO — que es justo el defecto que
        ' esta ola cierra, reproducido por la puerta de al lado.
        If origen Is Nothing OrElse origen.Record Is Nothing Then
            Throw New ArgumentException(
                "ClonDeBorrador necesita un borrador CON record: sin él no hay árbol que copiar y el " &
                "editor abriría vacío en vez de con la copia que el usuario pidió.", NameOf(origen))
        End If
        Return ClonDesdeCopia(origen.Record.Copia(), formIDNuevo)
    End Function

    '==============================================================================================
    ' Copiar y comparar
    '==============================================================================================

    Public Function Clone() As ArmoDraft
        ' ⛔ `Clone` es la TERCERA puerta: también CONSTRUYE un borrador, y `Copia()` puede
        ' devolver Nothing por los mismos tres caminos. Su resultado se registra en producción —
        ' `_openSnapshot = _draft.Clone()`, y `RevertOrDiscardCurrentDraft` lo vuelve a meter en el
        ' mapa que consultan el render y el guardado.
        Dim copiaClone = Record?.Copia()
        Borradores.ExigirRecord(copiaClone, "ARMO", "la copia del record falló: árbol o contexto nulos, o la firma no corresponde a esta vista")
        Return New ArmoDraft With {
            .Record = copiaClone,
            .FormID = FormID,
            .IsOverride = IsOverride,
            .IsNew = IsNew,
            .IsModified = IsModified
        }
    End Function

    ''' <summary>Mismo contenido que <paramref name="o"/>, sin mirar identidad ni estado.
    ''' <para>Se compara por los bytes que produciría cada uno. Comparar campo por campo obliga a
    ''' acordarse de todos, y el que se olvida es justo el que después aparece como "editado" sin que
    ''' nadie lo haya tocado.</para></summary>
    Public Function ContentEquals(o As ArmoDraft) As Boolean
        If o Is Nothing Then Return False
        If Record Is Nothing OrElse o.Record Is Nothing Then Return Record Is o.Record
        Return Record.MismoContenido(o.Record)
    End Function

End Class
