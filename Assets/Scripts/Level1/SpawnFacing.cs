using UnityEngine;

/// <summary>Al acomodarlo al azar, el objeto queda con su frente (+Z local) hacia el lado abierto de la repisa.</summary>
public class SpawnFacing : MonoBehaviour
{
    [Tooltip("Variación aleatoria (grados) alrededor de la orientación de frente.")]
    public float yawJitter = 6f;
}
