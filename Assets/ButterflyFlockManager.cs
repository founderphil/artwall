using UnityEngine;
using System.Collections.Generic;

public class ButterflyFlockManager : MonoBehaviour
{
    public GameObject butterflyPrefab;   
    public Transform player1Brush;
    public Transform player2Brush;
    public Transform player3Brush;

    public int numberOfButterflies = 20;
    public float spawnRadius = 30f;

    private List<ButterflyAgent> butterflies = new List<ButterflyAgent>();

    private void Start()
    {
        for (int i = 0; i < numberOfButterflies; i++)
        {
            Vector3 spawnPos = transform.position + Random.insideUnitSphere * spawnRadius;
            spawnPos.y = Mathf.Max(spawnPos.y, 0f); 

            GameObject newButterfly = Instantiate(butterflyPrefab, spawnPos, Quaternion.identity, transform);
            ButterflyAgent agent = newButterfly.GetComponent<ButterflyAgent>();
            if (agent != null)
            {
                agent.Initialize(GetRandomPlayerBrush(), this);
                butterflies.Add(agent);
            }
        }
    }

    private Transform GetRandomPlayerBrush()
    {
        Transform[] brushes = new Transform[] { player1Brush, player2Brush, player3Brush };
        return brushes[Random.Range(0, brushes.Length)];
    }

    private void Update()
    {
       // maybe something to add to a further game later ??
    }
}