using UnityEngine;
using TMPro;
using System.Collections;

public class WordFly : MonoBehaviour
{
    public float speed = 10f;
    private Vector3 targetPosition;
    private string word;
    private bool hasCollided = false;
    private RunWhisper parentScript;
    private GameObject cloneInstance;

    public void Initialize(string wordText, Vector3 target, float flightSpeed, RunWhisper parent, GameObject instance)
    {
        word = wordText;
        targetPosition = target;
        speed = flightSpeed;
        parentScript = parent;
        cloneInstance = instance; 
        
        TextMeshPro tmp = GetComponent<TextMeshPro>();
        if (tmp != null)
        {
            tmp.text = wordText;
            tmp.fontSize = 100f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.cyan;
        }
    }

    void Update()
    {
        if (!hasCollided)
        {
            transform.position = Vector3.MoveTowards(transform.position, targetPosition, speed * Time.deltaTime);
            if (Vector3.Distance(transform.position, targetPosition) < 0.1f)
            {
                SmashAndScatter();
            }
        }
    }

    void SmashAndScatter()
    {
        hasCollided = true;

        if (parentScript != null && !string.IsNullOrEmpty(word))
        {
            parentScript.CreateImprintedWordOnPlane(word);
        }

        TextMeshPro tmp = GetComponent<TextMeshPro>();
        if (tmp != null)
        {
            Debug.Log($"Starting fade-out for: {gameObject.name} (Instance ID: {gameObject.GetInstanceID()})");
            StartCoroutine(FadeOutImprintedText(tmp, 1f));
        }
        else
        {
            Debug.LogWarning($"TextMeshPro component missing on {gameObject.name}");
        }
    }

    IEnumerator FadeOutImprintedText(TextMeshPro textComponent, float duration)
    {
        // Double-check that the object is not a prefab
        if (gameObject.scene.name == null)
        {
            Debug.LogWarning($"Attempted to destroy a prefab asset: {gameObject.name}");
            yield break;
        }

        Color originalColor = textComponent.color;
        float elapsed = 0;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float alpha = Mathf.Lerp(1f, 0f, elapsed / duration);
            textComponent.color = new Color(originalColor.r, originalColor.g, originalColor.b, alpha);
            yield return null;
        }

        Debug.Log($"Destroying instance: {cloneInstance.name} (Instance ID: {cloneInstance.GetInstanceID()})");
        Destroy(cloneInstance); // Destroy the clone, not the prefab
    }
}