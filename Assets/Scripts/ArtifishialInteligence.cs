using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MyMathTools;


public class ArtifishialInteligence : MonoBehaviour
{

    [SerializeField] Rigidbody m_rb; 

    [Header("Tunable parameters")]
    float swimSpeed = 10f;
    float randomRotationVariance = 0.1f;
    float frequencyPDObstacleAvoidance = 1.0f;
    float dampingPDObstacleAvoidance = 1.0f;
    float frequencyPDalignment = 1.0f;
    float dampingPDalignment = 1.0f;

    float frequencyPDCoM = 1.0f;
    float dampingPDCoM = 1.0f;

    
    float swarmSearchRadius = 10.0f;
    float collisionDetectionRange = 10.0f;
    int numberOfRays = 5;
    float alignmentCoefficient = 1.0f;
    float avoidanceCoefficient = 10.0f;
    float randomTorqueCoefficient = 1.0f;

    bool drawRayToNearFish = true;

    bool drawCollisionAvoidanceRays = true;

    public FishSettings fishSettings;



    [Header("Internal")]
    string my_species = "Fish";
    float mySize;
    GameObject[] otherFish;
    GameObject[] nearFish = new GameObject[1]; 

    List<Vector3> raysInFront;
    Collider m_Collider;

    Vector3 avoidanceDirection;
    Vector3 avoidanceTorquePID;
    Vector3 alignmentTorquePID;
    Vector3 swarmCenterTorquePID;
    
    // bitshift to fish layer
    int fishLayerMask;



    // Start is called before the first frame update
    void Start()
    {

        GameObject fishSettingsObject = GameObject.FindGameObjectWithTag("FishSettings");
        fishSettings = fishSettingsObject.GetComponent<FishSettings>();


        m_rb = GetComponent<Rigidbody>();
        m_Collider = GetComponent<Collider>();
        

        fishLayerMask = (1 << LayerMask.NameToLayer("Fish"));
        // This casts rays only against colliders in layer 3.
        // But to collide against everything except layer 3, use the ~ operator because it inverts a bitmask.
        fishLayerMask = ~fishLayerMask;
        fishLayerMask = 0;
    
        // Extents are the half-widths of the box.
        // I only care about the thickness
        Vector3 mySizeVector = m_Collider.bounds.extents;
        mySize = Mathf.Max(Mathf.Max(mySizeVector.x, mySizeVector.y), mySizeVector.z);;


        otherFish = GameObject.FindGameObjectsWithTag(my_species);
        // I am a fish, but I am not other fish
        RemoveMyselfFromOtherFishArray();
        //Debug.Log("otherFish.Length = " + otherFish.Length);
        if(otherFish.Length != 0)
        {
            nearFish = findNearFish();
        }
        
    }
    
    void FixedUpdate() 
    {
        avoidanceTorquePID = Vector3.zero;
        alignmentTorquePID = Vector3.zero;
        swarmCenterTorquePID = Vector3.zero;

        // List of directions in a hemishere in front of the fish (global referential)
        Quaternion lookOrientationLocal = Quaternion.FromToRotation(Vector3.up, Vector3.forward);
        Quaternion lookOrientationGlobal = m_rb.rotation * lookOrientationLocal;
        raysInFront = Utils.fibonacciSphere(numberOfRays, lookOrientationGlobal);

        if(otherFish.Length != 0)
        {
            nearFish = findNearFish(); 

            // Alignment
            if(nearFish.Length > 1)
            {
                // I like to swim where my friends swim
                
                // Find the average orientation of the fish around me
                Quaternion swarmMeanOrientationRotation = CalculateSwarmalignment(nearFish);

                Vector3 meanPosition = CalculateSwarmCoM(nearFish);
                Vector3 meanPositionDirection = meanPosition - m_rb.position;
                float distance = meanPositionDirection.magnitude;
                Quaternion swarmOrientationToCenter = Quaternion.FromToRotation(Vector3.forward, meanPositionDirection);

                float Tmax = 100f;
                // Find the torque I need to apply to get to that orientation 
                alignmentTorquePID = CalculatePDTorque(swarmMeanOrientationRotation, frequencyPDalignment, dampingPDalignment);
                //Debug.Log("frequencyPDCoM = " + frequencyPDCoM);
                swarmCenterTorquePID = CalculatePDTorque(swarmOrientationToCenter, frequencyPDCoM, dampingPDCoM);
                float T = -Tmax + distance*2*Tmax/swarmSearchRadius;
                //swarmCenterTorquePID *= T;
            }
        }
        
        if(drawRayToNearFish)
            DrawRayToNearFish();
        
        // Avoidance
        if(isHeadingForColision())
        {
            // Find direction I want to swim in
            avoidanceDirection = noObstacleDirection();
            // Find the orientation I need to swim in that direction
            Quaternion avoidanceOrientation = Quaternion.FromToRotation(Vector3.forward, avoidanceDirection);
            // Find the torque I need to apply to get to that orientation
            avoidanceTorquePID = CalculatePDTorque(avoidanceOrientation, frequencyPDObstacleAvoidance, dampingPDObstacleAvoidance);
            
            //Debug.DrawRay(m_rb.transform.position, avoidanceTorquePID, Color.cyan);
        }
        


        /*
        // Adds a bit of spice to the movement, otherwise all fish would just swim in the swarm
        // Random noise is accumulated into a torque vector. Noise is normal -> random walk, mean 0
        // When I applied noise directly to torque the movement was jerky. This functions as an integrator to filter out high frequencies
        noiseVelocityRandomWalk = AddNoiseToVector(noiseVelocityRandomWalk);
        noiseVelocityRandomWalk = noiseVelocityRandomWalk.normalized*randomRotationVariance;
        */

        Vector3 noiseTorque = new Vector3(NextGaussian(0, randomRotationVariance), 
                                        NextGaussian(0, randomRotationVariance), 
                                        NextGaussian(0, randomRotationVariance));
        // Coefficients have only relative values, they do not increase the magnitude of the torque
        float sumOfCoefficients = alignmentCoefficient + avoidanceCoefficient + randomTorqueCoefficient;
        
        Vector3 totalTorque = alignmentCoefficient/sumOfCoefficients * alignmentTorquePID +
                             avoidanceCoefficient/sumOfCoefficients * avoidanceTorquePID + 
                             randomTorqueCoefficient/sumOfCoefficients * swarmCenterTorquePID;

        // There is significant angular drag on m_rb
        m_rb.AddTorque (totalTorque);

        // Swim forward
        // There is significant drag from the water which slows down the fish
        //m_rb.AddForce(swimSpeed*m_rb.transform.forward, ForceMode.VelocityChange);
        m_rb.AddForce(swimSpeed*m_rb.transform.forward, ForceMode.Force);

    }

    void DrawRayToNearFish()
    {
        foreach (GameObject fish in nearFish)
        {
            Debug.DrawLine(m_rb.transform.position, fish.transform.position, Color.green);
        }
    }
    
    bool isHeadingForColision(){


        Ray ray = new Ray(m_rb.transform.position, m_rb.transform.forward);
        RaycastHit hitInfo;

        // Cast a ray in ray direction and check for hit
        if(Physics.SphereCast(ray, mySize, out hitInfo, collisionDetectionRange))
        {
            if(hitInfo.rigidbody != m_rb)
            {
                if(drawCollisionAvoidanceRays)
                    Debug.DrawRay(m_rb.transform.position, hitInfo.distance*m_rb.transform.forward, Color.red);
                // Dont count myself as an obstacle
                return true;
            }
            //Debug.Log(hitInfo.distance);

        }
        
        return false;
    }

    Vector3 noObstacleDirection(){

        foreach (Vector3 direction in raysInFront)
        {
            Ray ray = new Ray(m_rb.transform.position, direction);

            // Cast a ray in ray direction and check for hit
            // if there is no obstacle, return that direction
            // directions in pointsOnASphere are sorted by distance from the center of the hemisphere, 
            // so the first one is the closest to straight direction
            if(!Physics.SphereCast(ray, mySize, collisionDetectionRange))
            {

                //Debug.DrawRay(m_rb.transform.position, collisionDetectionRange*direction, Color.blue);
                return direction;
            }
        }
        return Vector3.zero;
    }


    private void Update() {
        SetSettings();
    }

    
    void SetSettings()
    {   


        swimSpeed = fishSettings.swimSpeed;
        randomRotationVariance = fishSettings.randomRotationVariance;
        frequencyPDObstacleAvoidance = fishSettings.frequencyPDObstacleAvoidance;
        dampingPDObstacleAvoidance = fishSettings.dampingPDObstacleAvoidance;
        frequencyPDalignment = fishSettings.frequencyPDalignment;
        dampingPDalignment = fishSettings.dampingPDalignment;
        frequencyPDCoM = fishSettings.frequencyPDCoM;
        dampingPDCoM = fishSettings.dampingPDCoM;
        
        swarmSearchRadius = fishSettings.swarmSearchRadius;
        collisionDetectionRange = fishSettings.collisionDetectionRange;


        alignmentCoefficient = fishSettings.alignmentCoefficient;
        avoidanceCoefficient = fishSettings.avoidanceCoefficient;
        randomTorqueCoefficient = fishSettings.randomTorqueCoefficient;
        numberOfRays = fishSettings.numberOfRays;


    }

    void RemoveMyselfFromOtherFishArray()
    {
        // Utility function 
        List<GameObject> newOtherFish = new List<GameObject>();
        for (int i = 0; i < otherFish.Length; i++)
        {
            if(otherFish[i] != gameObject)
                newOtherFish.Add(otherFish[i]);
        }
        otherFish = newOtherFish.ToArray();
    }



    /*
    private void OnDrawGizmos() {
        // Obstacle avoidance draw rays
        // Doesnt actually do anything, its just used to visualize the obstacle avoidance rays
        for (int i = 0; i < numberOfRays; i++)
        {
            for (int j = 0; j < numberOfRays; j++)
            {
                float phi = i/(float)numberOfRays*Mathf.PI;
                float theta = j/(float)numberOfRays*Mathf.PI/2 + Mathf.PI/2.0f;
                Spherical localDir = new Spherical(1, phi, theta);
                Vector3 localDir_cart = CoordConvert.SphericalToCartesian(localDir);

                Quaternion q_wb = m_rb.transform.rotation; // body to world


                Vector3 direction = q_wb * localDir_cart;
                //Gizmos.DrawRay(m_rb.transform.position, direction);
            }
        }

        Gizmos.DrawRay(m_rb.transform.position, m_rb.transform.rotation * Vector3.up);

    }
    */

    Vector3 CalculateSwarmCoM(GameObject[] fishArray)
    {
            Vector3 meanPosition = Vector3.zero;
            // What are the other fish doing?
            for (int i = 0; i < fishArray.Length; i++)
            {
                // Calculate mean rotation of all the other fish
                meanPosition += fishArray[i].transform.position;
            }
            
            meanPosition = meanPosition / fishArray.Length;
            return meanPosition;
    }

    Quaternion CalculateSwarmalignment(GameObject[] fishArray)
    {
            float w=.0f,x=0.0f,y=0.0f,z=0.0f;
            // What are the other fish doing?
            for (int i = 0; i < fishArray.Length; i++)
            {
                // Calculate mean rotation of all the other fish
                w+=fishArray[i].transform.rotation.w;
                x+=fishArray[i].transform.rotation.x;
                y+=fishArray[i].transform.rotation.y;
                z+=fishArray[i].transform.rotation.z;
            }
            
            Quaternion meanRotation = new Quaternion(w/fishArray.Length, x/fishArray.Length, y/fishArray.Length, z/fishArray.Length);
            return meanRotation;
    }

    /*
    void SwarmCohesionAndSeparation()
    {   
        Vector3 repelForce = new Vector3(0,0,0);
        Vector3 attactForce = new Vector3(0,0,0);
        // What are the other fish doing?
        for (int i = 0; i < nearFish.Length; i++)
        {
                // Each fish looks at all the other fish each time.fixedDeltaTime -> O(n^2) 
            // Attract/Repel force
            float d = Vector3.Distance(nearFish[i].transform.position, m_rb.transform.position);
            Vector3 dir = nearFish[i].transform.position - m_rb.transform.position;
            repelForce += dir/Mathf.Pow(d,2); // separation force
            attactForce += dir/Mathf.Pow(d,1); // cohesion force
        }
        

        m_rb.AddForce(-repelForceMultiplier*repelForce); // Not too close to others
        m_rb.AddForce(attractForceMultiplier*attactForce); // Not too far from others
    }

    */





    Vector3 AddNoiseToVector(Vector3 v)
    {
        v.x += NextGaussian(0, randomRotationVariance);
        v.y += NextGaussian(0, randomRotationVariance);
        v.z += NextGaussian(0, randomRotationVariance);
        return v;
    }
    Quaternion AddNoiseToRotation(Quaternion q)
    {
        q.w += NextGaussian(0, randomRotationVariance);
        q.x += NextGaussian(0, randomRotationVariance);
        q.y += NextGaussian(0, randomRotationVariance);
        q.z += NextGaussian(0, randomRotationVariance);
        return q;
    }

    // dont @ me
    Vector3 CalculatePDTorque(Quaternion desiredRotation, float frequency, float damping)
    {
        float kp = (6f*frequency)*(6f*frequency)* 0.25f;
        float kd = 4.5f*frequency*damping;
        float dt = Time.fixedDeltaTime;
        float g = 1 / (1 + kd * dt + kp * dt * dt);
        float ksg = kp * g;
        float kdg = (kd + kp * dt) * g;
        Vector3 xVec;
        float xMag;
        Quaternion q = desiredRotation * Quaternion.Inverse(transform.rotation);
        // Q can be the-long-rotation-around-the-sphere eg. 350 degrees
        // We want the equivalant short rotation eg. -10 degrees
        // Check if rotation is greater than 190 degees == q.w is negative
        if (q.w < 0)
        {
            // Convert the quaterion to eqivalent "short way around" quaterion
            q.x = -q.x;
            q.y = -q.y;
            q.z = -q.z;
            q.w = -q.w;
        }
        
        q.ToAngleAxis (out xMag, out xVec);
        xVec.Normalize ();
        xVec *= Mathf.Deg2Rad;
        Vector3 pidv = kp * xVec * xMag - kd * m_rb.angularVelocity;
        Quaternion rotInertia2World = m_rb.inertiaTensorRotation * transform.rotation;
        pidv = Quaternion.Inverse(rotInertia2World) * pidv;
        pidv.Scale(m_rb.inertiaTensor);
        pidv = rotInertia2World * pidv;
        return pidv;
    }



    GameObject[] findNearFish()
    {
        //Debug.Log("other fish.Length: " + otherFish.Length);
        float[] distances = new float[otherFish.Length];
        List<int> index_of_close_fish = new List<int>();

        for (int i = 0; i < otherFish.Length; i++)
        {
            distances[i] = Vector3.Distance(otherFish[i].transform.position, m_rb.transform.position);
            
            if(distances[i] < swarmSearchRadius)
                index_of_close_fish.Add(i);
        }

        
        GameObject[] closeFish = new GameObject[index_of_close_fish.Count];
        for (int i = 0; i < index_of_close_fish.Count; i++)
        {
            closeFish[i] = otherFish[index_of_close_fish[i]];
        }

        return closeFish;

    }


    public static float NextGaussian() {
        float v1, v2, s;
        do {
            v1 = 2.0f * Random.Range(0f,1f) - 1.0f;
            v2 = 2.0f * Random.Range(0f,1f) - 1.0f;
            s = v1 * v1 + v2 * v2;
        } while (s >= 1.0f || s == 0f);
        s = Mathf.Sqrt((-2.0f * Mathf.Log(s)) / s);
    
        return v1 * s;
    }

    public static float NextGaussian(float mean, float standard_deviation)
    {
        return mean + NextGaussian() * standard_deviation;
    }
}
