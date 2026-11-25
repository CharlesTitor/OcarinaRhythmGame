using System.Collections.Generic;
using UnityEngine;
using TMPro;

/// <summary>
/// Juego de ritmo con Arduino y MPR121.
/// Dibuja los carriles y las notas usando cuadrados generados en memoria.
/// Las notas aparecen al azar y el script calcula el porcentaje global de aciertos.
/// No necesita sprites externos ni patrones de canciones.
/// </summary>
public class JuegoRitmoMPR121 : MonoBehaviour
{
    // MPR121SerialBridge debe estar en el mismo GameObject que este script.
    private MPR121SerialBridge puenteSerial;

    // Cámara principal usada para ajustar la pista a lo que se ve en pantalla.
    private Camera camaraPrincipal;

    // Sprite cuadrado que se genera en memoria y se reutiliza para toda la pista.
    private Texture2D texturaCircular;
    private Sprite spriteCircular;
    private Sprite spriteCuadrado;

    // Notas que se encuentran actualmente en la escena.
    private readonly List<NotaEnEscena> notasActivas =
        new List<NotaEnEscena>();

    // Centro horizontal de cada carril, en coordenadas del mundo.
    private float[] posicionesXCarriles;

    // Total de carriles, tomado de MPR121SerialBridge.
    private int cantidadCarriles;

    // Altura de la línea donde se evalúan los aciertos.
    private float posicionLineaAciertoY;

    // Altura desde donde aparecen las notas nuevas.
    private float posicionInicioNotasY;

    // Centro de la cámara en el plano XY.
    private Vector2 centroCamara;

    // Tiempo en que debe aparecer la siguiente nota.
    private float tiempoSiguienteNota;

    // Evita repetir el mismo carril en dos notas consecutivas cuando hay más de uno.
    private int carrilNotaAnterior = -1;

    // Contadores del porcentaje global de aciertos.
    [Header("Porcentaje de aciertos")]
    [Tooltip("Cantidad de notas que llegaron correctamente a la línea de acierto.")]
    public int totalAciertos;

    [Tooltip("Cantidad de notas que pasaron la línea sin ser acertadas.")]
    public int totalFallos;

    [Tooltip("Porcentaje: aciertos / (aciertos + fallos) * 100.")]
    [Range(0f, 100f)]
    public float porcentajeAcierto;

    [Header("Movimiento de las notas")]
    [Tooltip("Velocidad descendente de las notas, en unidades del mundo por segundo.")]
    [Min(0.1f)]
    public float velocidadNotas = 4f;

    [Tooltip("Tiempo entre la aparición de una nota y la siguiente.")]
    [Min(0.2f)]
    public float segundosEntreNotas = 1.2f;

    [Header("Zona de acierto")]
    [Tooltip("Distancia máxima entre la nota y la línea para registrar un acierto.")]
    [Min(0.05f)]
    public float margenAcierto = 0.35f;

    [Header("Tamaño de las notas")]
    [Min(0.1f)]
    public float tamanoNota = 0.65f;
    
    [Header("Puntuacion")]
    [SerializeField] private TMP_Text textoPuntuacion;
    [Min(0)] public int puntosPorAcierto=100;
    public int puntuacion;

    // Colores de los carriles 0 a 3.
    // Con cuatro carriles: rojo, verde, amarillo y azul.
    private readonly Color[] coloresCarril =
    {
        new Color(0.95f, 0.15f, 0.15f),
        new Color(0.15f, 0.75f, 0.25f),
        new Color(0.95f, 0.80f, 0.10f),
        new Color(0.15f, 0.45f, 0.95f)
    };

    /// <summary>
    /// Guarda el objeto visual y el número del carril de una nota activa.
    /// </summary>
    private class NotaEnEscena
    {
        public GameObject objeto;
        public int carril;

        public NotaEnEscena(GameObject objetoNota, int numeroCarril)
        {
            objeto = objetoNota;
            carril = numeroCarril;
        }
    }

    /// <summary>
    /// Unity ejecuta Start una vez al iniciar este objeto.
    /// Busca los componentes y dibuja los carriles antes de iniciar las notas.
    /// </summary>
    private void Start()
    {
        // Busca el puente serial en este mismo GameObject.
        puenteSerial = GetComponent<MPR121SerialBridge>();

        if (puenteSerial == null)
        {
            Debug.LogError(
                "Falta MPR121SerialBridge en el mismo GameObject que JuegoRitmoMPR121."
            );
            enabled = false;
            return;
        }

        // Obtiene la cámara que tenga la etiqueta MainCamera.
        camaraPrincipal = Camera.main;

        if (camaraPrincipal == null)
        {
            Debug.LogError("No se encontró una cámara con la etiqueta MainCamera.");
            enabled = false;
            return;
        }

        if (!camaraPrincipal.orthographic)
        {
            Debug.LogError(
                "JuegoRitmoMPR121 necesita una cámara Orthographic para ajustar la pista."
            );
            enabled = false;
            return;
        }

        // Este script tiene cuatro colores definidos, por eso admite de 1 a 4 carriles.
        cantidadCarriles = puenteSerial.cantidadCarriles;

        if (cantidadCarriles < 1 || cantidadCarriles > coloresCarril.Length)
        {
            Debug.LogError(
                "Configura cantidadCarriles entre 1 y " +
                coloresCarril.Length +
                " en MPR121SerialBridge."
            );
            enabled = false;
            return;
        }

        // Crea el cuadrado reutilizable y dibuja la pista.
        CrearSpriteCuadrado();
        CrearSpriteCirculo();
        CrearPista();

        // La primera nota aparece 1.5 segundos después de iniciar la escena.
        tiempoSiguienteNota = Time.time + 1.5f;

        // Reinicia las estadísticas al comenzar una partida.
        totalAciertos = 0;
        totalFallos = 0;
        porcentajeAcierto = 0f;
        
        puntuacion=0;
        ActualizarTextoPantalla();

        Debug.Log(
            "Pista creada con " + cantidadCarriles +
            " carriles. Las notas comenzarán cuando Arduino se conecte."
        );
    }

    /// <summary>
    /// Update mueve las notas y crea nuevas notas aleatorias mientras Arduino
    /// está conectado. Los carriles permanecen dibujados aunque Arduino no conecte.
    /// </summary>
    private void Update()
    {
        // Sin conexión no se crean ni se mueven notas.
        if (puenteSerial == null || !puenteSerial.Conectado)
        {
            return;
        }

        MoverNotas();

        // Al alcanzar el tiempo programado se crea una nota aleatoria.
        if (Time.time >= tiempoSiguienteNota)
        {
            CrearNota();
            tiempoSiguienteNota = Time.time + segundosEntreNotas;
        }
    }

    /// <summary>
    /// Se ejecuta después de Update y revisa si Arduino recibió un toque nuevo.
    /// </summary>
    private void LateUpdate()
    {
        if (puenteSerial == null || !puenteSerial.Conectado)
        {
            return;
        }

        for (int carril = 0; carril < cantidadCarriles; carril++)
        {
            if (puenteSerial.FuePresionadoEsteFrame(carril))
            {
                EvaluarPulsacion(carril);
            }
        }
    }

    /// <summary>
    /// Crea una textura de un píxel y la convierte en un Sprite cuadrado.
    /// </summary>
    private void CrearSpriteCuadrado()
    {
        spriteCuadrado = Sprite.Create(
            Texture2D.whiteTexture,
            new Rect(0, 0, Texture2D.whiteTexture.width, Texture2D.whiteTexture.height),
            new Vector2(0.5f, 0.5f),
            Texture2D.whiteTexture.width
        );
    }
    private void CrearSpriteCirculo()
    {
        texturaCircular= new Texture2D(64, 64, TextureFormat.RGBA32, false);

        for (int x=0;x< 64;x++)
        {
            for (int y=0;y< 64;y++)
            {
                float distancia = Vector2.Distance(new Vector2(x, y), new Vector2(31.5f, 31.5f));

                if (distancia<=31.5f)
                {
                    texturaCircular.SetPixel(x, y, Color.white);
                }
                else
                {
                    texturaCircular.SetPixel(x, y, Color.clear);
                }
            }
        }

        texturaCircular.Apply();

        spriteCircular= Sprite.Create(texturaCircular,new Rect(0, 0, 64, 64),new Vector2(0.5f, 0.5f),64f);
    }

    /// <summary>
    /// Calcula los centros de los carriles con respecto a la cámara y dibuja
    /// sus fondos, marcadores de acierto y la línea horizontal.
    /// </summary>
    private void CrearPista()
    {
        float mitadAlto = camaraPrincipal.orthographicSize;
        float mitadAncho = mitadAlto * camaraPrincipal.aspect;
        float margenLateral = 0.35f;

        float anchoPista = (mitadAncho * 2f) - (margenLateral * 2f);
        float anchoCarril = anchoPista / cantidadCarriles;

        // Se usa el centro de cámara para que la pista también funcione
        // cuando la cámara no está colocada en (0, 0).
        centroCamara = new Vector2(
            camaraPrincipal.transform.position.x,
            camaraPrincipal.transform.position.y
        );

        posicionesXCarriles = new float[cantidadCarriles];

        posicionLineaAciertoY = centroCamara.y - mitadAlto + 1.1f;
        posicionInicioNotasY = centroCamara.y + mitadAlto - 0.5f;

        for (int carril = 0; carril < cantidadCarriles; carril++)
        {
            posicionesXCarriles[carril] =
                centroCamara.x - mitadAncho +
                margenLateral + anchoCarril * (carril + 0.5f);

            Color color = coloresCarril[carril];

            // Fondo vertical semitransparente del carril.
            CrearForma(
                "FondoCarril_" + carril,
                new Vector3(posicionesXCarriles[carril], centroCamara.y, 0f),
                new Vector2(anchoCarril * 0.92f, mitadAlto * 2f),
                new Color(color.r, color.g, color.b, 0.22f),
                0
            );

            // Marcador del color del carril donde se deben tocar las notas.
            CrearForma(
                "MarcadorAcierto_" + carril,
                new Vector3(posicionesXCarriles[carril], posicionLineaAciertoY, 0f),
                new Vector2(anchoCarril * 0.55f, 0.16f),
                color,
                2
            );
        }

        CrearForma(
            "LineaDeAcierto",
            new Vector3(centroCamara.x, posicionLineaAciertoY, 0f),
            new Vector2(anchoPista, 0.06f),
            Color.white,
            1
        );
    }

    /// <summary>
    /// Elige un carril al azar y crea una nota cuadrada en la parte superior.
    /// </summary>
    private void CrearNota()
    {
        int carrilElegido = Random.Range(0, cantidadCarriles);

        // Evita repetir el mismo carril consecutivamente cuando hay varios.
        if (cantidadCarriles > 1 && carrilElegido == carrilNotaAnterior)
        {
            carrilElegido = (carrilElegido + 1) % cantidadCarriles;
        }

        carrilNotaAnterior = carrilElegido;

        float mitadAnchoPista =
            camaraPrincipal.orthographicSize * camaraPrincipal.aspect;

        float anchoCarril =
            (mitadAnchoPista * 2f - 0.70f) / cantidadCarriles;

        GameObject objetoNota = CrearForma(
        "Nota_Carril_" + carrilElegido,
        new Vector3(
            posicionesXCarriles[carrilElegido],
            posicionInicioNotasY,
            0f),
        new Vector2(tamanoNota, tamanoNota),coloresCarril[carrilElegido],3,true);

        notasActivas.Add(new NotaEnEscena(objetoNota, carrilElegido));
    }

    /// <summary>
    /// Desplaza las notas hacia abajo y registra como fallo las que pasan la línea.
    /// </summary>
    private void MoverNotas()
    {
        for (int indice = notasActivas.Count - 1; indice >= 0; indice--)
        {
            NotaEnEscena nota = notasActivas[indice];

            nota.objeto.transform.Translate(
                Vector3.down * velocidadNotas * Time.deltaTime
            );

            float yNota = nota.objeto.transform.position.y;

            if (yNota < posicionLineaAciertoY - margenAcierto)
            {
                Debug.Log("FALLO: se perdió la nota del carril " + nota.carril + ".");
                Destroy(nota.objeto);
                notasActivas.RemoveAt(indice);

                totalFallos++;
                ActualizarPorcentajeAcierto();
            }
        }
    }

    /// <summary>
    /// Busca una nota del carril tocado y comprueba si llegó a la zona de acierto.
    /// </summary>
    private void EvaluarPulsacion(int carril)
    {
        NotaEnEscena notaMasCercana = null;
        float distanciaMasCercana = float.MaxValue;

        for (int indice = 0; indice < notasActivas.Count; indice++)
        {
            NotaEnEscena nota = notasActivas[indice];

            if (nota.carril != carril)
            {
                continue;
            }

            float distancia = Mathf.Abs(
                nota.objeto.transform.position.y - posicionLineaAciertoY
            );

            if (distancia < distanciaMasCercana)
            {
                distanciaMasCercana = distancia;
                notaMasCercana = nota;
            }
        }

        if (notaMasCercana != null && distanciaMasCercana <= margenAcierto)
        {
            Debug.Log("ACIERTO: carril " + carril + ".");

            Destroy(notaMasCercana.objeto);
            notasActivas.Remove(notaMasCercana);

            totalAciertos++;
            puntuacion += puntosPorAcierto;
            ActualizarPorcentajeAcierto();
        }
        else
        {
            Debug.Log("ERROR: no había una nota en la zona de acierto del carril " + carril + ".");
        }
    }

    /// <summary>
    /// Calcula y muestra el porcentaje global:
    /// aciertos / (aciertos + fallos) * 100.
    /// </summary>
    private void ActualizarPorcentajeAcierto()
    {
        int notasEvaluadas = totalAciertos + totalFallos;

        if (notasEvaluadas == 0)
        {
            porcentajeAcierto = 0f;
        }
        else
        {
            porcentajeAcierto =
                (float)totalAciertos / notasEvaluadas * 100f;
        }

        Debug.Log(
            "Porcentaje de aciertos: " +
            porcentajeAcierto.ToString("F1") +
            "% (" + totalAciertos + " aciertos de " +
            notasEvaluadas + " notas evaluadas)."
        );
        ActualizarTextoPantalla();
    }

    private void ActualizarTextoPantalla()
    {
        if (textoPuntuacion!=null)
        {
            textoPuntuacion.text ="Puntuación: "+puntuacion +"\nPrecisión: "+porcentajeAcierto.ToString("F1") + "%";
        }
    }

    /// <summary>
    /// Crea un objeto 2D con el Sprite cuadrado generado en memoria.
    /// </summary>
    private GameObject CrearForma(
        string nombre,
        Vector3 posicion,
        Vector2 tamano,
        Color color,
        int ordenDibujo,bool esCircular=false)
    {
        GameObject forma = new GameObject(nombre);
        forma.transform.position = posicion;

        SpriteRenderer dibujador = forma.AddComponent<SpriteRenderer>();
       if (esCircular)
       {
           dibujador.sprite=spriteCircular;
       }
       else
       {
           dibujador.sprite=spriteCuadrado;
       }
        dibujador.color = color;
        dibujador.sortingOrder = ordenDibujo;

        forma.transform.localScale = new Vector3(tamano.x, tamano.y, 1f);

        return forma;
    }

    /// <summary>
    /// Libera el Sprite y la textura que este script creó en memoria.
    /// </summary>
    private void OnDestroy()
    {
        if (spriteCircular != null)
        {
            Destroy(spriteCircular);
        }

        if (texturaCircular != null)
        {
            Destroy(texturaCircular);
        }
    }
}