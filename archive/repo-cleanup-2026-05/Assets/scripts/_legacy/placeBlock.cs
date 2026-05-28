using UnityEngine;

public class placeBlock : MonoBehaviour
{
    [Tooltip("Prefab to spawn when the ground is clicked")]
    public GameObject blockPrefab;

    [Tooltip("Layers considered as ground for placement")]
    public LayerMask groundLayer = ~0;

    [Tooltip("Vertical offset to place the block above the hit point")]
    public float heightOffset = 0.5f;

    [Header("Progression")]
    public int clickCount = 0;
    public int totalBlockCount = 0;

    [Tooltip("How many precise clicks occur before chaotic spread begins")]
    public int initialPreciseClicks = 15;

    [Tooltip("How quickly the number of blocks per click grows after the precise phase")]
    public float growthExponent = 0.48f;

    [Tooltip("How strongly extra block count scales after the precise phase")]
    public float growthMultiplier = 0.55f;

    [Tooltip("Maximum spread radius for spawned blocks")]
    public float maxSpread = 3f;

    [Tooltip("How many total blocks trigger faster mold-like spreading")]
    public int moldSpreadThreshold = 60;

    [Tooltip("How many mold blocks spawn automatically when spreading begins")]
    public int moldSpawnPerTick = 1;

    [Tooltip("Radius around existing blocks where mold can spread")]
    public float moldSpreadRadius = 1.5f;

    [Tooltip("Base interval in seconds between automatic growth ticks")]
    public float autoSpreadBaseInterval = 4f;

    [Tooltip("Minimum interval in seconds between automatic growth ticks")]
    public float autoSpreadMinInterval = 0.8f;

    [Tooltip("How much the automatic growth interval shrinks as more blocks exist")]
    public float autoSpreadIntervalReduction = 0.02f;

    [Tooltip("Maximum random scale multiplier for blocks as clicks increase")]
    public float maxScaleFactor = 1.8f;

    private readonly System.Collections.Generic.List<GameObject> spawnedBlocks = new System.Collections.Generic.List<GameObject>();
    private float autoSpreadTimer = 0f;
    private int autoSpreadTickCount = 0;

    void Update()
    {
        if (Input.GetMouseButtonDown(0))
        {
            clickCount++;
            SpawnBlockAtMousePosition();
        }

        autoSpreadTimer += Time.deltaTime;
        if (autoSpreadTimer >= GetAutoSpreadInterval())
        {
            autoSpreadTimer = 0f;
            SpreadBlocksAutomatically();
        }
    }

    void SpawnBlockAtMousePosition()
    {
        if (blockPrefab == null)
        {
            Debug.LogWarning("PlaceBlock: blockPrefab is not set.");
            return;
        }

        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);

        if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, groundLayer))
        {
            int blocksToSpawn = CalculateBlocksPerClick();
            float spread = CalculateSpread();
            float verticalChaos = CalculateVerticalChaos();
            float clickScale = DetermineBlockScale();

            Vector3 anchorPosition = hit.point + Vector3.up * heightOffset;
            Quaternion anchorRotation = Quaternion.identity;

            if (clickCount > initialPreciseClicks)
            {
                anchorPosition += new Vector3(
                    Random.Range(-0.04f, 0.04f),
                    Random.Range(0f, 0.02f),
                    Random.Range(-0.04f, 0.04f)
                );

                anchorRotation = Quaternion.Euler(
                    Random.Range(-6f, 6f),
                    Random.Range(-10f, 10f),
                    Random.Range(-6f, 6f)
                );
            }

            var anchorBlock = Instantiate(blockPrefab, anchorPosition, anchorRotation);
            anchorBlock.transform.localScale = Vector3.one * clickScale;
            spawnedBlocks.Add(anchorBlock);
            totalBlockCount++;

            for (int i = 1; i < blocksToSpawn; i++)
            {
                Vector3 randomOffset = new Vector3(
                    Random.Range(-spread, spread),
                    0,
                    Random.Range(-spread, spread)
                );

                float stackOffset = i * 0.08f;
                float randomHeight = Random.Range(0f, verticalChaos);
                Vector3 spawnPosition = anchorPosition + randomOffset + Vector3.up * (stackOffset + randomHeight);

                var block = Instantiate(blockPrefab, spawnPosition, Quaternion.identity);
                block.transform.localScale = Vector3.one * clickScale;
                spawnedBlocks.Add(block);
                totalBlockCount++;
            }
        }
    }

    int CalculateBlocksPerClick()
    {
        if (clickCount <= initialPreciseClicks)
        {
            return 1;
        }

        float growth = Mathf.Pow(clickCount - initialPreciseClicks, growthExponent) * growthMultiplier;
        int extraBlocks = Mathf.FloorToInt(growth);
        if (Random.value < (growth - extraBlocks))
        {
            extraBlocks++;
        }

        return Mathf.Max(1, 1 + extraBlocks);
    }

    float CalculateSpread()
    {
        if (clickCount <= initialPreciseClicks)
        {
            return 0.05f;
        }

        return Mathf.Clamp((clickCount - initialPreciseClicks) * 0.12f, 0.2f, maxSpread);
    }

    float CalculateVerticalChaos()
    {
        if (clickCount <= initialPreciseClicks)
        {
            return 0f;
        }

        return Mathf.Clamp((clickCount - initialPreciseClicks) * 0.01f, 0.02f, 0.5f);
    }

    float DetermineBlockScale()
    {
        if (clickCount <= initialPreciseClicks)
        {
            return 1f;
        }

        float growth = Mathf.Clamp01((float)(clickCount - initialPreciseClicks) / 40f);
        float maxScale = 1f + growth * (maxScaleFactor - 1f);
        return Random.Range(1f, maxScale);
    }

    float GetAutoSpreadInterval()
    {
        float progress = Mathf.Clamp01((float)autoSpreadTickCount / 20f + (float)totalBlockCount / 120f);
        float interval = Mathf.Lerp(autoSpreadBaseInterval, autoSpreadMinInterval, progress);
        return Mathf.Clamp(interval, autoSpreadMinInterval, autoSpreadBaseInterval);
    }

    void SpreadBlocksAutomatically()
    {
        if (clickCount <= initialPreciseClicks || spawnedBlocks.Count == 0)
        {
            return;
        }

        autoSpreadTickCount++;

        int extraSpawnsFromTicks = Mathf.FloorToInt(autoSpreadTickCount * 0.08f);
        int extraSpawnsFromBlocks = Mathf.FloorToInt(Mathf.Max(0, totalBlockCount - moldSpreadThreshold) * 0.02f);
        int spawnCount = 1 + Mathf.Max(0, extraSpawnsFromTicks + extraSpawnsFromBlocks);

        if (totalBlockCount >= moldSpreadThreshold)
        {
            spawnCount += moldSpawnPerTick;
        }

        for (int i = 0; i < spawnCount; i++)
        {
            int index = Random.Range(0, spawnedBlocks.Count);
            GameObject sourceBlock = spawnedBlocks[index];
            if (sourceBlock == null)
            {
                spawnedBlocks.RemoveAt(index);
                continue;
            }

            Vector3 moldOffset = new Vector3(
                Random.Range(-moldSpreadRadius, moldSpreadRadius),
                Random.Range(0f, 0.25f),
                Random.Range(-moldSpreadRadius, moldSpreadRadius)
            );

            Vector3 moldPosition = sourceBlock.transform.position + moldOffset;
            var moldBlock = Instantiate(blockPrefab, moldPosition, Quaternion.identity);
            moldBlock.transform.localScale = Vector3.one * DetermineBlockScale();
            spawnedBlocks.Add(moldBlock);
            totalBlockCount++;
        }
    }
}
