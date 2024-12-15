using UnityEngine;

public class ButterflyAgent : MonoBehaviour
{
    public float speed = 5f;
    public float rotationSpeed = 2f;
    public float attractionStrength = 1.0f; 
    public float sphereRadius = 0.5f;        
    public float comfortableDistance = 2f;   
    //public float cameraZOffset = -0.5f;

    private Transform target;
    private ButterflyFlockManager manager;
    private Vector3 velocity;
    private float seed;

    public void Initialize(Transform target, ButterflyFlockManager manager)
    {
        this.target = target;
        this.manager = manager;
        velocity = Random.insideUnitSphere * speed;
        seed = Random.Range(0f, 100f); // A unique offset for each butterfly

        if (comfortableDistance <= sphereRadius)
        {
            comfortableDistance = sphereRadius + 1.0f;
        }
        //vary speed and comfortable distance slightly for more erratic movement
        speed += Random.Range(-1f, 1f);
        comfortableDistance += Random.Range(-1.5f, 1.5f);
        rotationSpeed += Random.Range(-1f, 1f);
        
    }

    private void Update()
    {
        if (target == null)
        {
            Wander();
        }
        else
        {
            Vector3 directionToTarget = target.position - transform.position;
            float distance = directionToTarget.magnitude;

            if (distance < comfortableDistance)
            {
                HoverAroundTarget(directionToTarget);
            }
            else
            {
                MoveTowardsTarget(directionToTarget);
            }
        }
        transform.position += velocity * Time.deltaTime;
       // transform.position = new Vector3(transform.position.x, transform.position.y, transform.position.z + cameraZOffset); //offset

        //butterfly realism: directional alignment, natural variantion, combining rotations, smooth transitions - still not great.
        if (velocity.sqrMagnitude > 0.01f)
        {
            Quaternion targetRot = Quaternion.LookRotation(velocity.normalized, Vector3.up);
            Quaternion randomOffset = Quaternion.Euler(
                0, 
                (Mathf.PerlinNoise(Time.time * 0.5f + seed, seed) - 0.5f) * 20f, 
                (Mathf.PerlinNoise(seed, Time.time * 0.5f + seed) - 0.5f) * 20f
            );
            targetRot = targetRot * randomOffset;
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotationSpeed * Time.deltaTime);
        }
    }

    private void MoveTowardsTarget(Vector3 directionToTarget)
{
    Vector3 desiredVelocity = directionToTarget.normalized * speed;
    
    Vector3 noise = new Vector3(
        Mathf.PerlinNoise(Time.time + seed, seed) - 0.5f,
        Mathf.PerlinNoise(seed, Time.time + seed * 2f) - 0.5f,
        0f
    ) * 0.5f; 
    desiredVelocity += noise;

    velocity = Vector3.Lerp(velocity, desiredVelocity, attractionStrength * Time.deltaTime);
}

    private void HoverAroundTarget(Vector3 directionToTarget)
    {
         
        Vector3 fromTarget = directionToTarget.normalized * comfortableDistance;
        Vector3 perpendicular = Vector3.Cross(directionToTarget, Vector3.up).normalized;
        float circleOffset = 0.5f;
        float timeWithSeed = Time.time + seed;
        Vector3 circlePoint = target.position 
            + directionToTarget.normalized * comfortableDistance
            + perpendicular * Mathf.Sin(timeWithSeed) * circleOffset
            + Vector3.up * Mathf.Cos(timeWithSeed) * 0.25f;

        Vector3 desiredVelocity = (circlePoint - transform.position).normalized * (speed * 0.5f);
        velocity = Vector3.Lerp(velocity, desiredVelocity, attractionStrength * Time.deltaTime);
    }

    private void Wander()
    {
        Vector3 randomDir = Random.insideUnitSphere;
        velocity = Vector3.Lerp(velocity, randomDir * speed, 0.1f * Time.deltaTime);
    }
}