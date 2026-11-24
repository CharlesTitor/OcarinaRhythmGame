using System.Collections.Generic;
using UnityEngine;

/*
 JuegoRitmoMPR121

 Genera una pista sencilla estilo Guitar Hero:

   1. Dibuja carriles verticales.
   2. Coloca una zona horizontal de acierto.
   3. Genera notas cuadradas que bajan por carriles aleatorios.
   4. Consulta las pulsaciones recibidas por MPR121SerialBridge.
   5. Imprime EXCELENTE o ERROR en la Console.

 No necesita sprites descargados ni prefabs.
 El script crea un sprite cuadrado blanco en memoria y cambia su color
 para formar carriles, marcadores y notas.
*/
public class JuegoRitmoMPR121 : MonoBehaviour
{

    // CONFIGURACIÓN DEL JUEGO

    [Header("Movimiento de las notas")]

    [Tooltip("Velocidad vertical a la que bajan las notas, en unidades por segundo.")]
    [Min(0.1f)]
    public float velocidadNotas = 4f;

    [Tooltip("Segundos entre la creación de una nota y la siguiente.")]
    [Min(0.2f)]
    public float segundosEntreNotas = 1.2f;

    [Header("Zona de acierto")]

    [Tooltip("Distancia máxima entre el centro de una nota y la línea de acierto " +
        "para considerar que el toque fue EXCELENTE.")]
    [Min(0.05f)]
    public float margenExcelente = 0.35f;

    // REFERENCIAS A OTROS COMPONENTES Y OBJETOS   

    // Este componente recibe los eventos T,carril y R,carril del Arduino.
    private MPR121SerialBridge puenteSerial;

    // La cámara principal permite calcular los bordes visibles y colocar
    // carriles y notas dentro de la pantalla.
    private Camera camaraPrincipal;

    // Sprite cuadrado blanco creado en memoria; se reutiliza para todos
    // los elementos visuales del juego.
    private Sprite spriteCuadrado;

    // Textura de 1 x 1 píxel usada para crear spriteCuadrado.
    private Texture2D texturaCuadrada;



    // DATOS DE LA PISTA


    // Guarda los objetos de nota que están actualmente en la escena.
    private readonly List<NotaEnEscena> notasActivas =
        new List<NotaEnEscena>();

    // Posición horizontal, en unidades del mundo de Unity, para cada carril.
    private float[] posicionesCarrilesX;

    // Número de carriles tomado del componente MPR121SerialBridge.
    private int cantidadCarriles;

    // Coordenada vertical de la línea donde se deben tocar las notas.
    private float posicionLineaAciertoY;

    // Coordenada vertical donde aparecen las notas nuevas.
    private float posicionInicioNotasY;

    // Momento del reloj de Unity en que debe aparecer la siguiente nota.
    private float tiempoSiguienteNota;

    // Se usa para evitar que dos notas seguidas aparezcan en el mismo carril
    // cuando el juego tiene más de un carril.
    private int carrilNotaAnterior = -1;


    // Colores de referencia para los carriles.
    // Si hay más carriles que colores, los colores vuelven a empezar.
    private readonly Color[] coloresCarril =
 {
    new Color(0.95f, 0.15f, 0.15f), // Carril 0: rojo.
    new Color(0.15f, 0.75f, 0.25f), // Carril 1: verde.
    new Color(0.95f, 0.80f, 0.10f),  // Carril 2: amarillo.
    new Color(0.15f, 0.45f, 0.95f)  // Carril 3: azul
};


    /*
      Se hace porque ambos programas expresan la intensidad de los colores con escalas distintas:
- Unity usa decimales entre 0 y 1. Por ejemplo, 0.15 representa el 15 % de la intensidad máxima
    del rojo. de paint a unity se divide el valor / 255
- Paint usa enteros entre 0 y 255. El 255 es la intensidad máxima de un canal de color; hay
    256 valores posibles contando desde 0.
La multiplicación solo sirve para convertir de la escala de Unity a la de Paint. 
    Si tienes un color RGB de Paint y quieres escribirlo en Unity, divides cada canal entre 255.
     
    */
    /*
     // CLASE INTERNA: NotaEnEscena
      Guarda los datos de una nota individual:
        - objeto: el cuadrado que se ve en pantalla.
        - carril: el carril al que pertenece.

     Esta clase no es un MonoBehaviour; solo organiza información dentro
     de JuegoRitmoMPR121.
    */

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



    // Unity llama a Start() una vez al iniciar el componente.
    // Aquí se buscan los componentes necesarios y se construye la pista visual.

    private void Start()
    {
        /*
        GetComponent<T>() busca en este mismo objeto de la Jerarquía un
         componente del tipo indicado.
        
         Por eso MPR121SerialBridge y JuegoRitmoMPR121 deben estar agregados
         al mismo objeto ControladorSerial.
        */
        puenteSerial = GetComponent<MPR121SerialBridge>();


        if (puenteSerial == null)
        {
            Debug.LogError("No se encontró MPR121SerialBridge en este objeto. " +
                "Agrega ambos scripts a ControladorSerial.");

            // enabled = false desactiva este componente.
            // Así Update() y LateUpdate() no intentarán usar una referencia
            // que no existe.
            enabled = false;
            return;
        }

        // Camera.main busca la cámara marcada con la etiqueta MainCamera.
        camaraPrincipal = Camera.main;

        if (camaraPrincipal == null)
        {
            Debug.LogError("No se encontró una cámara con la etiqueta MainCamera.");
            enabled = false;
            return;
        }

        // Toma la cantidad configurada en el Inspector del puente serial.
        // Debe coincidir con la cantidad de botones que envía Arduino.
        cantidadCarriles = puenteSerial.cantidadCarriles;

        if (cantidadCarriles < 1 || cantidadCarriles > 12)
        {
            Debug.LogError("Cantidad Carriles debe estar entre 1 y 12.");
            enabled = false;
            return;
        }

        // Crea un cuadrado blanco reutilizable para los elementos visuales.
        CrearSpriteCuadrado();

        // Calcula las posiciones y dibuja la pista y la línea de acierto.
        CrearPista();

        // La primera nota aparecerá un poco después de iniciar el juego.
        tiempoSiguienteNota = Time.time + 1.5f;

        Debug.Log("Pista creada con " + cantidadCarriles +
            " carriles. Esperando conexión con Arduino...");
    }


    // Esta función mueve las notas, retira las que ya se fallaron y genera
    // nuevas notas a intervalos regulares.

    private void Update()
    {
        // No inicia el juego de notas hasta que MPR121SerialBridge haya abierto
        // el puerto serial correctamente.
        if (puenteSerial == null || !puenteSerial.Conectado)
        {
            return;
        }

        // Mueve las notas que ya existen hacia la línea de acierto.
        MoverNotas();

        // Time.time es el tiempo transcurrido desde que comenzó la escena.
        // Cuando alcanza el momento programado, crea una nota nueva.
        if (Time.time >= tiempoSiguienteNota)
        {
            CrearNota();

            // Programa el momento de aparición de la siguiente nota.
            tiempoSiguienteNota = Time.time + segundosEntreNotas;
        }
    }


    //LateUpdate()
    /*
     Unity ejecuta LateUpdate() después de que todos los Update() terminaron.
     MPR121SerialBridge procesa los mensajes seriales en Update(), así que aquí
     consultamos las pulsaciones después de que el puente tuvo oportunidad
     de procesarlas.
    */
    private void LateUpdate()
    {
        if (puenteSerial == null || !puenteSerial.Conectado)
        {
            return;
        }

        // Revisa todos los carriles activos.
        for (int carril = 0; carril < cantidadCarriles; carril++)
        {
            // FuePresionadoEsteFrame() consulta si llegó un evento T para
            // ese carril durante el frame actual.
            if (puenteSerial.FuePresionadoEsteFrame(carril))
            {
                EvaluarPulsacion(carril);
            }
        }
    }


    /*
        CrearSpriteCuadrado()

        Crea una textura de un píxel y la convierte en un Sprite.
        No se carga un archivo de imagen externo: el script genera el cuadrado
        blanco en memoria y luego lo reutiliza con distintos tamaños y colores.
    */
    private void CrearSpriteCuadrado()
    {
        // Texture2D crea una textura nueva.
        // Los valores 1, 1 significan que la textura mide un píxel de ancho
        // por un píxel de alto.
        texturaCuadrada = new Texture2D(1, 1);

        // SetPixel asigna el color al píxel situado en (0, 0).
        texturaCuadrada.SetPixel(0, 0, Color.white);

        // Apply aplica los cambios de píxeles para que Unity pueda usar la textura.
        texturaCuadrada.Apply();

        // Sprite.Create crea un Sprite a partir de la textura.
        // Rect(0, 0, 1, 1) usa toda la textura.
        // El pivote (0.5, 0.5) pone el centro del Sprite en su punto medio.
        // Pixels Per Unit = 1 hace que el cuadrado mida aproximadamente una
        // unidad del mundo antes de escalarlo.
        spriteCuadrado = Sprite.Create(texturaCuadrada, new Rect(0, 0, 1, 1),
            new Vector2(0.5f, 0.5f), 1f);
    }

    /*
     CrearPista()
    
     Calcula el espacio visible de la cámara y crea:
       - Un carril vertical por cada botón.
       - Un marcador de color en cada carril.
       - Una línea horizontal de acierto.
   */
    private void CrearPista()
    {
        // orthographicSize es la mitad de la altura visible de una cámara 2D.
        float mitadAlto = camaraPrincipal.orthographicSize;

        // aspect es la proporción ancho/alto de la ventana del juego.
        // Multiplicarla por la altura visible calcula la mitad del ancho visible.
        float mitadAncho = mitadAlto * camaraPrincipal.aspect;

        // Deja un margen en los lados para que los carriles no queden pegados
        // al borde de la pantalla.
        float margenLateral = 0.35f;

        // Calcula el ancho disponible para todos los carriles.
        float anchoPista = (mitadAncho * 2f) - (margenLateral * 2f);

        // Divide el espacio disponible por la cantidad de carriles.
        float anchoCarril = anchoPista / cantidadCarriles;

        // Crea un arreglo para guardar la posición X central de cada carril.
        posicionesCarrilesX = new float[cantidadCarriles];

        // La línea de acierto queda cerca de la parte inferior de la pantalla.
        posicionLineaAciertoY = -mitadAlto + 1.1f;

        // Las notas aparecen cerca de la parte superior.
        posicionInicioNotasY = mitadAlto - 0.5f;

        for (int carril = 0; carril < cantidadCarriles; carril++)
        {
            // Calcula el centro horizontal del carril.
            posicionesCarrilesX[carril] =
                -mitadAncho + margenLateral + anchoCarril * (carril + 0.5f);

            Color color = ObtenerColorCarril(carril);

            // Crea la franja vertical del carril.
            SpriteRenderer fondo = CrearForma(
                "FondoCarril_" + carril,
                new Vector3(posicionesCarrilesX[carril], 0f, 0f),
                new Vector3(anchoCarril * 0.92f, mitadAlto * 2f, 1f),
                new Color(color.r, color.g, color.b, 0.18f), 0);

            // Un color con alpha 0.18 es semitransparente:
            // permite distinguir carriles sin tapar las notas.
            fondo.color = new Color(color.r, color.g, color.b, 0.18f);

            // Crea el marcador fijo donde se espera tocar el botón.
            CrearForma("MarcadorAcierto_" + carril,
                new Vector3(posicionesCarrilesX[carril], posicionLineaAciertoY, 0f),
                new Vector3(anchoCarril * 0.55f, 0.16f, 1f), color, 2);
        }

        // Crea una línea horizontal que cruza toda la pista.
        // Esta línea es la referencia visual del momento de acierto.
        CrearForma("LineaDeAcierto", new Vector3(0f, posicionLineaAciertoY, 0f),
            new Vector3(anchoPista, 0.06f, 1f), Color.white, 1);
    }


    /*
        CrearNota()

        Elige un carril aleatorio y crea una nota cuadrada en la parte superior.
        La nota se agrega a notasActivas para que MoverNotas() pueda desplazarla.
      */
    private void CrearNota()
    {
        int carrilElegido = Random.Range(0, cantidadCarriles);

        // Si hay más de un carril, evita repetir el carril inmediatamente.
        // Random.Range(int, int) incluye el primer valor y excluye el segundo.
        if (cantidadCarriles > 1 && carrilElegido == carrilNotaAnterior)
        {
            carrilElegido = (carrilElegido + 1) % cantidadCarriles;
        }

        carrilNotaAnterior = carrilElegido;

        Color color = ObtenerColorCarril(carrilElegido);

        GameObject objetoNota = CrearForma("Nota_Carril_" + carrilElegido,
            new Vector3(posicionesCarrilesX[carrilElegido], posicionInicioNotasY, 0f),
            new Vector3(0.55f, 0.38f, 1f), color, 3).gameObject;

        // Agrega el objeto y su carril a la lista de notas que siguen en juego.
        notasActivas.Add(new NotaEnEscena(objetoNota, carrilElegido));

        //Debug.Log("Apareció una nota en el carril " + carrilElegido);
    }

    /*
     MoverNotas()
    
     Desplaza cada nota hacia abajo.
    Si una nota pasa la zona de acierto sin ser tocada, registra un ERROR
     y elimina su objeto de la escena.
  */
    private void MoverNotas()
    {
        // Se recorre la lista desde el final hacia el inicio porque se pueden
        // quitar elementos mientras se recorre.
        for (int indice = notasActivas.Count - 1; indice >= 0; indice--)
        {
            NotaEnEscena nota = notasActivas[indice];

            // Translate cambia la posición del objeto en la dirección indicada.
            //
            // Vector3.down significa (0, -1, 0), es decir, hacia abajo.
            // Time.deltaTime es el tiempo transcurrido desde el frame anterior.
            // Multiplicarlo hace que la velocidad sea similar aunque cambien
            // los cuadros por segundo de la computadora.
            nota.objeto.transform.Translate(
                Vector3.down * velocidadNotas * Time.deltaTime);

            float yNota = nota.objeto.transform.position.y;

            // Si el centro de la nota bajó más allá del margen de acierto,
            // el jugador dejó pasar esa nota.
            if (yNota < posicionLineaAciertoY - margenExcelente)
            {
                //Debug.LogError("ERROR: Se perdió la nota del carril " + nota.carril);

                // Destroy() programa la eliminación del GameObject al final
                // del frame actual.
                Destroy(nota.objeto);

                // RemoveAt() quita esta nota de la lista de notas pendientes.
                notasActivas.RemoveAt(indice);
            }
        }
    }


    /*
     EvaluarPulsacion(carril)
    
     Busca la nota más cercana en el carril presionado.
    
     Si hay una nota dentro del margenExcelente, registra EXCELENTE.
     Si no hay nota a tiempo en ese carril, registra ERROR.
    */
    private void EvaluarPulsacion(int carril)
    {
        NotaEnEscena notaMasCercana = null;
        float distanciaMasCercana = float.MaxValue;

        // Busca entre todas las notas activas una que pertenezca al carril
        // presionado y esté lo más cerca posible de la línea de acierto.
        for (int indice = 0; indice < notasActivas.Count; indice++)
        {
            NotaEnEscena nota = notasActivas[indice];

            if (nota.carril != carril)
            {
                continue;
            }

            // Mathf.Abs() obtiene el valor absoluto: mide la distancia sin
            // importar si la nota está arriba o abajo de la línea.
            float distancia = Mathf.Abs(
                nota.objeto.transform.position.y - posicionLineaAciertoY);

            if (distancia < distanciaMasCercana)
            {
                distanciaMasCercana = distancia;
                notaMasCercana = nota;
            }
        }


        // Si se encontró una nota y está suficientemente cerca de la línea,
        // el toque fue en el momento correcto.
        if (notaMasCercana != null &&
            distanciaMasCercana <= margenExcelente)
        {
            Debug.Log("EXCELENTE: acertaste en el carril " + carril);

            // Destruye la nota acertada.
            Destroy(notaMasCercana.objeto);

            // Remove() quita de la lista el objeto de nota indicado.
            notasActivas.Remove(notaMasCercana);
        }
        else
        {
            // Esto ocurre si se tocó el carril equivocado, demasiado pronto
            // o demasiado tarde.
            Debug.LogError(
                "ERROR: No había una nota en el punto de acierto del carril " + carril);
        }
    }


    // =========================================================================
    // CrearForma(...)
    //
    // Crea un GameObject con SpriteRenderer y le asigna el cuadrado creado
    // en CrearSpriteCuadrado().
    //
    // Se usa para crear fondos de carriles, marcadores, línea y notas.
    // =========================================================================
    private SpriteRenderer CrearForma(
        string nombre,
        Vector3 posicion,
        Vector3 escala,
        Color color,
        int ordenDibujo)
    {
        // new GameObject crea un objeto nuevo en la escena.
        GameObject forma = new GameObject(nombre);

        // AddComponent<SpriteRenderer>() agrega el componente que dibuja un
        // Sprite en una escena 2D.
        SpriteRenderer dibujador = forma.AddComponent<SpriteRenderer>();

        // Asigna el Sprite cuadrado generado por este mismo script.
        dibujador.sprite = spriteCuadrado;

        // Define el color que se verá en pantalla.
        dibujador.color = color;

        // sortingOrder decide qué elemento se dibuja delante de otro:
        // los números más altos aparecen encima de los más bajos.
        dibujador.sortingOrder = ordenDibujo;

        // Asigna la posición y el tamaño del objeto en el mundo 2D.
        forma.transform.position = posicion;
        forma.transform.localScale = escala;

        return dibujador;
    }


    // =========================================================================
    // ObtenerColorCarril(carril)
    //
    // Devuelve el color asignado al carril solicitado.
    // El operador % permite reutilizar los colores si se usan más carriles
    // que colores definidos.
    // =========================================================================

    private Color ObtenerColorCarril(int carril)
    {
        return coloresCarril[carril];
    }
    /*
    private Color ObtenerColorCarril(int carril)
    {
        return coloresCarril[carril % coloresCarril.Length];
    }
    */

    // =========================================================================
    // OnDestroy()
    //
    // Elimina la textura y el Sprite creados en memoria al cerrar o descargar
    // la escena. Destroy() libera esos recursos de Unity.
    // =========================================================================
    private void OnDestroy()
    {
        if (spriteCuadrado != null)
        {
            Destroy(spriteCuadrado);
        }

        if (texturaCuadrada != null)
        {
            Destroy(texturaCuadrada);
        }
    }
}