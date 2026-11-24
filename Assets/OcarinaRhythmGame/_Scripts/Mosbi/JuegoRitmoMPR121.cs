using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Controla la pista visual, el movimiento de las notas y su calificación.
/// MPR121SerialBridge recibe los datos que Arduino envía por el puerto serial.
/// Este script consulta ese componente para detectar nuevas pulsaciones.
/// </summary>
public class JuegoRitmoMPR121 : MonoBehaviour
{
    // Componente que lee los mensajes enviados por Arduino.
    // Debe estar en el mismo GameObject que este script.
    private MPR121SerialBridge puenteSerial;

    // Cámara marcada con la etiqueta MainCamera.
    // Se utiliza para calcular el tamaño visible de la escena.
    private Camera camaraPrincipal;

    // Textura de un píxel que se usa como base para dibujar cuadrados.
    private Texture2D texturaCuadrado;

    // Sprite cuadrado creado a partir de la textura anterior.
    // Se reutiliza para las notas, carriles y líneas.
    private Sprite spriteCuadrado;

    // Lista de las notas que existen actualmente en la escena.
    private readonly List<NotaEnEscena> notasActivas =
        new List<NotaEnEscena>();

    // Coordenada horizontal central de cada carril.
    private float[] posicionesXCarriles;

    // Cantidad de carriles que este juego usará.
    // Se copia desde el campo cantidadCarriles del puente serial.
    private int cantidadCarriles;

    // Coordenada Y de la línea que se debe alcanzar al tocar.
    private float posicionLineaAciertoY;

    // Coordenada Y donde aparece cada nota nueva.
    private float posicionInicioNotasY;

    // Tiempo de Unity en que debe aparecer la siguiente nota.
    private float tiempoSiguienteNota;

    // Evita repetir dos veces seguidas el mismo carril al generar notas,
    // siempre que haya más de un carril.
    private int ultimoCarrilGenerado = -1;

    // Permanece falso si Start encuentra un componente o una configuración faltante.
    private bool juegoPreparado = false;

    // sortingOrder controla qué elementos se dibujan delante de otros.
    private const int ORDEN_FONDO = 0;
    private const int ORDEN_NOTAS = 2;
    private const int ORDEN_LINEA_ACIERTO = 3;

    [Header("Movimiento y aparición de notas")]

    [Tooltip("Velocidad vertical de las notas en unidades del mundo por segundo.")]
    [Min(0.1f)]
    public float velocidadNotas = 4f;

    [Tooltip("Segundos que deben pasar entre la aparición de una nota y la siguiente.")]
    [Min(0.1f)]
    public float segundosEntreNotas = 1.2f;

    [Header("Ventana para acertar")]

    [Tooltip(
        "Distancia vertical máxima entre el centro de la nota y la línea de acierto. " +
        "Se mide en unidades del mundo de Unity.")]
    [Min(0.05f)]
    public float ventanaMaximaAcierto = 0.60f;

    [Header("Porcentajes de calificación")]

    [Tooltip("Porcentaje mínimo para Perfecto. El 100% corresponde al centro de la línea.")]
    [Range(0, 100)]
    public int porcentajePerfecto = 95;

    [Tooltip("Porcentaje mínimo para Great, si no se alcanzó Perfecto.")]
    [Range(0, 100)]
    public int porcentajeGreat = 85;

    [Tooltip("Porcentaje mínimo para Good, si no se alcanzó Great.")]
    [Range(0, 100)]
    public int porcentajeGood = 70;

    [Header("Colores de carril")]

    // Los índices del arreglo coinciden con los carriles:
    // 0 = rojo, 1 = verde, 2 = amarillo, 3 = azul.
    // Cada componente de Color va de 0 a 1. Por ejemplo,
    // 0.95 equivale aproximadamente a 242 en la escala de 0 a 255.
    private readonly Color[] coloresCarril =
    {
        new Color(0.95f, 0.15f, 0.15f), // Carril 0: rojo
        new Color(0.15f, 0.75f, 0.25f), // Carril 1: verde
        new Color(0.95f, 0.80f, 0.10f), // Carril 2: amarillo
        new Color(0.15f, 0.45f, 0.95f)  // Carril 3: azul
    };

    /// <summary>
    /// Registro de una nota activa: el GameObject que se dibuja
    /// y el número de carril al que pertenece.
    /// </summary>
    private class NotaEnEscena
    {
        public GameObject objeto;
        public int carril;

        /// <summary>
        /// Constructor: asigna los valores recibidos a los campos del registro.
        /// </summary>
        public NotaEnEscena(GameObject objetoNota, int numeroCarril)
        {
            objeto = objetoNota;
            carril = numeroCarril;
        }
    }

    /// <summary>
    /// Start se ejecuta una sola vez al iniciar este objeto.
    /// Busca componentes, valida la cantidad de carriles y crea la pista.
    /// </summary>
    private void Start()
    {
        Debug.Log(" Entramos al START");
        // GetComponent<T>() busca un componente del tipo T en este mismo GameObject.
        puenteSerial = GetComponent<MPR121SerialBridge>();

        // Camera.main busca la cámara que tenga la etiqueta MainCamera.
        camaraPrincipal = Camera.main;

        // Sin el puente no se pueden recibir las pulsaciones de Arduino.
        if (puenteSerial == null)
        {
            Debug.LogError(
                "Falta MPR121SerialBridge. Agrégalo al mismo GameObject " +
                "que contiene JuegoRitmoMPR121."
            );
            return; // Termina Start porque no se puede configurar el juego.
        }

        // Sin cámara principal no se puede calcular el espacio visible.
        if (camaraPrincipal == null)
        {
            Debug.LogError(
                "No se encontró una cámara con la etiqueta MainCamera."
            );
            return;
        }

        // Para este juego 2D se recomienda Projection = Orthographic.
        if (!camaraPrincipal.orthographic)
        {
            Debug.LogWarning(
                "Configura la cámara con Projection = Orthographic para este juego 2D."
            );
        }

        // Lee del puente serial la cantidad de sensores/carriles seleccionada.
        // Para cuatro sensores, cantidadCarriles debe valer 4 en ese componente.
        cantidadCarriles = puenteSerial.cantidadCarriles;

        // Hay cuatro colores definidos, por lo que se permiten de 1 a 4 carriles.
        if (cantidadCarriles < 1 || cantidadCarriles > coloresCarril.Length)
        {
            Debug.LogError(
                "cantidadCarriles debe estar entre 1 y " +
                coloresCarril.Length +
                ". Configúralo en MPR121SerialBridge."
            );
            return;
        }

        // Mathf.Max(a, b) devuelve el mayor de los dos valores.
        // El mínimo de 0.05 evita una ventana igual a cero o negativa;
        // más adelante la distancia se divide entre esta ventana.
        ventanaMaximaAcierto = Mathf.Max(0.05f, ventanaMaximaAcierto);

        CrearSpriteCuadrado();
        CrearPista();

        // Time.time son los segundos transcurridos desde que inició la escena.
        // Se espera 1.5 segundos para que el jugador se prepare.
        tiempoSiguienteNota = Time.time + 1.5f;

        // Update y LateUpdate solo ejecutarán su lógica después de esta preparación.
        juegoPreparado = true;

        Debug.Log(
            "Juego preparado con " + cantidadCarriles +
            " carriles. Esperando la conexión de Arduino."
        );
    }

    /// <summary>
    /// Update se ejecuta una vez por frame.
    /// Mueve notas y crea nuevas mientras el puente esté conectado.
    /// </summary>
    private void Update()
    {
        if (!juegoPreparado)
        {
            return;
        }

        // Con la conexión desconectada se pausa el juego para no perder notas.
        if (!puenteSerial.Conectado)
        {
            return;
        }

        MoverNotas();

        // Al cumplirse el tiempo programado, se crea una nota.
        if (Time.time >= tiempoSiguienteNota)
        {
            CrearNota();

            // Programa la próxima nota sumando el intervalo al tiempo actual.
            tiempoSiguienteNota = Time.time + segundosEntreNotas;
        }
    }

    /// <summary>
    /// LateUpdate se ejecuta después de Update en cada frame.
    /// Consulta si el puente detectó el inicio de una pulsación.
    /// </summary>
    private void LateUpdate()
    {
        if (!juegoPreparado || !puenteSerial.Conectado)
        {
            return;
        }

        // Con cuatro carriles, revisa los índices 0, 1, 2 y 3.
        for (int carril = 0; carril < cantidadCarriles; carril++)
        {
            // FuePresionadoEsteFrame(carril) devuelve true solo en el frame
            // en que comienza la pulsación; no repite true por mantener el dedo.
            if (puenteSerial.FuePresionadoEsteFrame(carril))
            {
                EvaluarPulsacion(carril);
            }
        }
    }

    /// <summary>
    /// Crea una textura blanca de 1 por 1 píxeles y un Sprite cuadrado.
    /// </summary>
    private void CrearSpriteCuadrado()
    {
        // Texture2D(ancho, alto) crea una textura de las dimensiones indicadas.
        texturaCuadrado = new Texture2D(1, 1);

        // SetPixel(x, y, color) cambia el píxel en las coordenadas indicadas.
        // En una textura 1x1, el único píxel está en (0, 0).
        texturaCuadrado.SetPixel(0, 0, Color.white);

        // Apply() aplica a la textura los cambios hechos con SetPixel.
        texturaCuadrado.Apply();

        // Sprite.Create convierte la textura en un Sprite.
        // Rect(0,0,ancho,alto) selecciona toda la textura.
        // Vector2(0.5,0.5) pone el pivote del Sprite en su centro.
        spriteCuadrado = Sprite.Create(
            texturaCuadrado,
            new Rect(0, 0, texturaCuadrado.width, texturaCuadrado.height),
            new Vector2(0.5f, 0.5f)
        );

        spriteCuadrado.name = "SpriteCuadradoGeneradoEnEjecucion";
    }

    /// <summary>
    /// Calcula posiciones y dibuja carriles, zonas de acierto y línea horizontal.
    /// </summary>
    private void CrearPista()
    {
        // orthographicSize es la mitad de la altura que ve la cámara.
        float mitadAlto = camaraPrincipal.orthographicSize;

        // aspect es la relación ancho/alto. Multiplicarla por mitadAlto
        // calcula la mitad del ancho visible.
        float mitadAncho = mitadAlto * camaraPrincipal.aspect;

        // La pista usa el 80% del ancho de pantalla, dejando margen a los lados.
        float anchoTotalPista = mitadAncho * 2f * 0.80f;

        // Reparte el ancho de pista en partes iguales entre los carriles.
        float anchoCarril = anchoTotalPista / cantidadCarriles;

        // Reserva un espacio para la coordenada X central de cada carril.
        posicionesXCarriles = new float[cantidadCarriles];

        // -mitadAlto es el límite inferior visible; +1.1 sube la línea.
        posicionLineaAciertoY = -mitadAlto + 1.1f;

        // mitadAlto es el límite superior; restar 0.5 coloca la nota dentro.
        posicionInicioNotasY = mitadAlto - 0.5f;

        for (int carril = 0; carril < cantidadCarriles; carril++)
        {
            // Obtiene el borde izquierdo y avanza hasta el centro del carril.
            float xCarril =
                -anchoTotalPista / 2f +
                anchoCarril * carril +
                anchoCarril / 2f;

            posicionesXCarriles[carril] = xCarril;

            // Dibuja el fondo vertical del carril con un color oscurecido.
            CrearForma(
                "FondoCarril_" + carril,
                new Vector3(xCarril, 0f, 0f),
                new Vector3(anchoCarril * 0.92f, mitadAlto * 2f, 1f),
                OscurecerColor(ObtenerColorCarril(carril), 0.28f),
                ORDEN_FONDO
            );

            // Dibuja un marcador corto del color del carril en la altura de acierto.
            CrearForma(
                "ZonaAciertoCarril_" + carril,
                new Vector3(xCarril, posicionLineaAciertoY, 0f),
                new Vector3(anchoCarril * 0.72f, 0.12f, 1f),
                ObtenerColorCarril(carril),
                ORDEN_LINEA_ACIERTO
            );
        }

        // Dibuja una línea blanca horizontal que indica la altura exacta de acierto.
        CrearForma(
            "LineaHorizontalDeAcierto",
            new Vector3(0f, posicionLineaAciertoY, 0f),
            new Vector3(anchoTotalPista, 0.035f, 1f),
            Color.white,
            ORDEN_LINEA_ACIERTO + 1
        );
    }

    /// <summary>
    /// Crea una nota nueva en un carril aleatorio y guarda su registro.
    /// </summary>
    private void CrearNota()
    {
        // Random.Range(enteroMin, enteroMax) incluye el mínimo y excluye el máximo.
        // Con cantidadCarriles = 4, devuelve 0, 1, 2 o 3.
        int carrilElegido = Random.Range(0, cantidadCarriles);

        // Si repite el carril anterior y hay varios carriles, avanza uno.
        // El operador % devuelve el residuo y permite volver al carril 0
        // después del último carril.
        if (cantidadCarriles > 1 &&
            carrilElegido == ultimoCarrilGenerado)
        {
            carrilElegido = (carrilElegido + 1) % cantidadCarriles;
        }

        ultimoCarrilGenerado = carrilElegido;

        // La nota aparece en el centro horizontal de su carril y arriba de la pantalla.
        Vector3 posicionNota = new Vector3(
            posicionesXCarriles[carrilElegido],
            posicionInicioNotasY,
            0f
        );

        GameObject objetoNota = CrearForma(
            "Nota_Carril_" + carrilElegido,
            posicionNota,
            new Vector3(0.55f, 0.38f, 1f),
            ObtenerColorCarril(carrilElegido),
            ORDEN_NOTAS
        );

        // Conserva el objeto y su carril para moverlo y evaluar la pulsación.
        notasActivas.Add(new NotaEnEscena(objetoNota, carrilElegido));
    }

    /// <summary>
    /// Mueve las notas hacia abajo y registra MISS si alguna pasa la ventana.
    /// </summary>
    private void MoverNotas()
    {
        // Se recorre la lista hacia atrás porque dentro del ciclo se pueden borrar notas.
        for (int i = notasActivas.Count - 1; i >= 0; i--)
        {
            NotaEnEscena nota = notasActivas[i];

            // Si una nota no tiene objeto válido, se elimina su registro y se continúa.
            if (nota == null || nota.objeto == null)
            {
                notasActivas.RemoveAt(i);
                continue;
            }

            // Vector3.down es la dirección (0,-1,0), hacia abajo.
            // Time.deltaTime son los segundos transcurridos desde el frame anterior.
            // Multiplicar velocidad por deltaTime produce movimiento por segundo
            // y evita que la velocidad dependa de los frames por segundo.
            Vector3 desplazamiento =
                Vector3.down * velocidadNotas * Time.deltaTime;

            // Translate suma el desplazamiento a la posición actual de la nota.
            nota.objeto.transform.Translate(desplazamiento);

            // La diferencia indica la posición de la nota respecto a la línea.
            float diferenciaY =
                nota.objeto.transform.position.y - posicionLineaAciertoY;

            // Si ya pasó por debajo de la línea más allá de la ventana,
            // se registra que no hubo pulsación a tiempo.
            if (diferenciaY < -ventanaMaximaAcierto)
            {
                Debug.LogError(
                    "MISS: no se presionó el sensor del carril " +
                    nota.carril + " dentro de la ventana de acierto."
                );

                // Destroy solicita a Unity eliminar el objeto visual.
                Destroy(nota.objeto);

                // RemoveAt elimina el registro de la lista usando su índice.
                notasActivas.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// Busca la nota más cercana del carril tocado, calcula precisión
    /// y muestra Perfecto, Great, Good o Miss en la consola.
    /// </summary>
    private void EvaluarPulsacion(int carril)
    {
        // Los índices válidos de un arreglo de cuatro carriles son 0, 1, 2 y 3.
        if (carril < 0 || carril >= cantidadCarriles)
        {
            Debug.LogError("Número de carril fuera del rango: " + carril);
            return;
        }

        int indiceNotaMasCercana = -1;
        float distanciaMasCercana = float.MaxValue;

        // Se recorren las notas activas para encontrar la más cercana
        // que pertenezca al carril que se acaba de tocar.
        for (int i = notasActivas.Count - 1; i >= 0; i--)
        {
            NotaEnEscena nota = notasActivas[i];

            // Limpia registros cuyo objeto ya no existe.
            if (nota == null || nota.objeto == null)
            {
                notasActivas.RemoveAt(i);
                continue;
            }

            // Ignora notas de los otros carriles.
            if (nota.carril != carril)
            {
                continue;
            }

            // Mathf.Abs(valor) devuelve el valor absoluto.
            // La resta puede ser negativa si la nota está debajo de la línea;
            // Abs la convierte en distancia positiva.
            float distanciaVertical = Mathf.Abs(
                nota.objeto.transform.position.y - posicionLineaAciertoY
            );

            // Si esta nota está más cerca que la anterior encontrada,
            // conserva su índice y su distancia.
            if (distanciaVertical < distanciaMasCercana)
            {
                distanciaMasCercana = distanciaVertical;
                indiceNotaMasCercana = i;
            }
        }

        // Si no hay ninguna nota de ese carril, la pulsación no coincide con nota.
        if (indiceNotaMasCercana == -1)
        {
            Debug.LogError(
                "MISS: se presionó el carril " + carril +
                ", pero no hay una nota de ese carril."
            );
            return;
        }

        // Si está más lejos que la ventana, la pulsación fue demasiado temprana.
        // La nota se conserva para que aún pueda acertarse después.
        if (distanciaMasCercana > ventanaMaximaAcierto)
        {
            Debug.LogError(
                "MISS: pulsación fuera de tiempo en el carril " + carril +
                ". La nota aún no está dentro de la ventana."
            );
            return;
        }

        // Convierte la distancia a una proporción entre 0 y 1:
        // distancia 0 produce 1 (100%); distancia igual a la ventana produce 0.
        float precisionNormalizada =
            1f - (distanciaMasCercana / ventanaMaximaAcierto);

        // Mathf.Clamp(valor, minimo, maximo) mantiene el valor dentro del rango.
        // Primer argumento: la proporción calculada arriba.
        // Segundo argumento: 0, límite inferior.
        // Tercer argumento: 1, límite superior.
        // Esto protege el cálculo de valores fuera del rango por redondeos.
        float precisionLimitada = Mathf.Clamp(
            precisionNormalizada,
            0f,
            1f
        );

        // Multiplica la proporción por 100 para convertirla a porcentaje.
        // RoundToInt redondea al entero más cercano; por ejemplo 94.6 pasa a 95.
        int porcentajePrecision = Mathf.RoundToInt(
            precisionLimitada * 100f
        );

        // Primero se comprueba el grado más alto para darle prioridad.
        if (porcentajePrecision >= porcentajePerfecto)
        {
            Debug.Log(
                "PERFECTO: carril " + carril +
                " | precisión: " + porcentajePrecision + "%"
            );
        }
        else if (porcentajePrecision >= porcentajeGreat)
        {
            Debug.Log(
                "GREAT: carril " + carril +
                " | precisión: " + porcentajePrecision + "%"
            );
        }
        else if (porcentajePrecision >= porcentajeGood)
        {
            Debug.Log(
                "GOOD: carril " + carril +
                " | precisión: " + porcentajePrecision + "%"
            );
        }
        else
        {
            // La pulsación quedó dentro de la ventana, pero no alcanzó el mínimo Good.
            Debug.LogError(
                "MISS: carril " + carril +
                " | precisión insuficiente: " + porcentajePrecision + "%"
            );
        }

        // Después de calificar, se destruye la nota aunque haya sido Miss.
        Destroy(notasActivas[indiceNotaMasCercana].objeto);

        // También se elimina su registro para que no se vuelva a calificar.
        notasActivas.RemoveAt(indiceNotaMasCercana);
    }

    /// <summary>
    /// Crea un GameObject cuadrado con posición, escala, color y orden de dibujo.
    /// </summary>
    private GameObject CrearForma(
        string nombre,
        Vector3 posicion,
        Vector3 escala,
        Color color,
        int ordenDibujo)
    {
        // Crea un objeto vacío con el nombre recibido.
        GameObject forma = new GameObject(nombre);

        // position establece la ubicación del objeto en coordenadas del mundo.
        forma.transform.position = posicion;

        // localScale define el ancho, alto y profundidad.
        forma.transform.localScale = escala;

        // AddComponent<SpriteRenderer>() añade el componente que dibuja sprites 2D.
        SpriteRenderer renderer = forma.AddComponent<SpriteRenderer>();

        // Asigna el sprite cuadrado y el color que recibieron los parámetros.
        renderer.sprite = spriteCuadrado;
        renderer.color = color;

        // Define qué SpriteRenderer se dibuja encima de otro.
        renderer.sortingOrder = ordenDibujo;

        // Devuelve el objeto creado para que el método que lo llamó pueda usarlo.
        return forma;
    }

    /// <summary>
    /// Busca el color que corresponde al índice del carril.
    /// </summary>
    private Color ObtenerColorCarril(int carril)
    {
        // Un arreglo comienza en el índice 0; cada índice representa un carril.
        return coloresCarril[carril];
    }

    /// <summary>
    /// Reduce el brillo de un color para usarlo en el fondo de cada carril.
    /// </summary>
    private Color OscurecerColor(Color colorOriginal, float intensidad)
    {
        // Multiplica cada componente por intensidad.
        // Clamp01(valor) limita cada resultado al intervalo de 0 a 1.
        float rojo = Mathf.Clamp01(colorOriginal.r * intensidad);
        float verde = Mathf.Clamp01(colorOriginal.g * intensidad);
        float azul = Mathf.Clamp01(colorOriginal.b * intensidad);

        // Crea y devuelve otro Color con esos componentes y opacidad completa (1).
        return new Color(rojo, verde, azul, 1f);
    }

    /// <summary>
    /// OnDestroy se ejecuta cuando se destruye el componente o se descarga la escena.
    /// Libera el Sprite y la textura creados por este script.
    /// </summary>
    private void OnDestroy()
    {
        // Destroy elimina el recurso Sprite generado durante la ejecución.
        if (spriteCuadrado != null)
        {
            Destroy(spriteCuadrado);
        }

        // Destroy elimina la textura generada durante la ejecución.
        if (texturaCuadrado != null)
        {
            Destroy(texturaCuadrado);
        }
    }
}