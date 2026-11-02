using JetBrains.Annotations;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

public class Cursor : MonoBehaviour
{
    #region Ints/Floats/Bools/etc
    public enum GameState
    {
        Idle,
        UnitSelected,
        MenuOpen,
        AttackSubMenuOpen,
        AttackMode,
        SpawnerSelected,
        ResearchOpen,
        UnloadMode
    }


    public GameObject cursor;
    public GameObject PlayerUI;
    public GameObject SpawnUI;
    public GameObject ResearchUI;
    public RectTransform Menu;

    [Header("Action Menu")]
    [Tooltip("The Action button in the unit menu. Greyed out when the unit has nothing it can do right now")]
    public Button actionButton;
    [Tooltip("Action sub-menu buttons. Only the ones the selected unit has are shown, stacked in this order")]
    public GameObject primaryAButton;
    public GameObject secondaryAButton;
    public GameObject trapButton;
    public GameObject unloadButton;
    [Tooltip("How far right of the Action button the sub-menu sits, and the gap between its buttons")]
    public float actionMenuX = 160f;
    public float actionButtonSpacing = 40f;

    [Header("Camera Edge Scroll")]
    public bool enableEdgeScroll = true;
    public float edgeScrollBorder = 15f; // pixels from screen edge that trigger scrolling
    public float edgeScrollSpeed = 10f;  // world units per second

    [Header("Camera Bounds (optional)")]
    public bool useCameraBounds = false;
    public Vector2 cameraMinBounds;
    public Vector2 cameraMaxBounds;
    public bool isUIOpen => currentState == GameState.MenuOpen
                          || currentState == GameState.SpawnerSelected
                          || currentState == GameState.AttackSubMenuOpen
                          || currentState == GameState.ResearchOpen;

    private GameManager gm;
    private bool isMoving = false;

    private Mouse mouse;
    private GameState currentState = GameState.Idle;
    private UnitController selectedUnit;
    private UnitController.AttackType selectedAttackType = UnitController.AttackType.Primary;
    private UnitSpawner selectedSpawner;
    private Dictionary<Vector2, int> reachableTiles = new Dictionary<Vector2, int>();
    private List<Vector2> currentPath = new List<Vector2>();
    private bool hasHoveredTile = false;
    private Vector2 lastHoveredTile;

    #endregion

    // Start is called before the first frame update
    void Start()
    {
        mouse = Mouse.current;
        currentState = GameState.Idle;
        PlayerUI.SetActive(false);
        if (SpawnUI != null) SpawnUI.SetActive(false);
       // if (ResearchUI != null) ResearchUI.SetActive(false);

        GameObject obj = GameObject.Find("GameManager");
        gm = obj.GetComponent<GameManager>();
    }

    // Update is called once per frame
    void Update()
    {
        if (!isUIOpen) HandleEdgeScroll(); // the camera stays put while a menu is open
        RefreshMenuButtons();
        if (currentState == GameState.SpawnerSelected && (SpawnUI == null || !SpawnUI.activeSelf))
            currentState = GameState.Idle;
        if (currentState == GameState.ResearchOpen && (ResearchUI == null || !ResearchUI.activeSelf))
            currentState = GameState.Idle;
        if (currentState == GameState.MenuOpen && !PlayerUI.activeSelf)
            currentState = GameState.Idle;
        if (currentState == GameState.AttackSubMenuOpen && !PlayerUI.activeSelf)
            currentState = GameState.Idle;

        if (!isUIOpen && !isMoving && !Class.AbilityPlaying && gm.IsPlayerTurn)
        {
            UpdateCursorVisual();
            if (currentState == GameState.UnitSelected)
            {
                UpdateHoverPath();
            }
            if (mouse.leftButton.wasPressedThisFrame)
            {
                GameClick();
            }

            // Enter ends the turn. Skipped while a menu is open, because Enter is also the
            // UI "Submit" key and could press a highlighted menu button at the same time
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame))
            {
                EndTurnButton();
            }
        }
    }
        void UpdateCursorVisual()
        {
            Vector2 mouseScreenPos = mouse.position.ReadValue();
            RaycastHit2D hit = Physics2D.Raycast(Camera.main.ScreenToWorldPoint(mouseScreenPos), Vector2.zero);

            if (hit.collider != null)
            {
                float x = Mathf.Round(hit.point.x);
                float y = Mathf.Round(hit.point.y) + 0.75f;
                cursor.transform.position = new Vector2(x, y);
            }
        }

        void GameClick()
        {
            Vector2 mouseScreenPos = mouse.position.ReadValue();
            Vector2 mousePos = Camera.main.ScreenToWorldPoint(mouseScreenPos);
            Vector2 gridPos = new Vector2(Mathf.Round(mousePos.x), Mathf.Round(mousePos.y));
            //Debug.Log($"[Click] State: {currentState} | GridPos: {gridPos}");
            switch (currentState)
            {
                case GameState.Idle:
                    TrySelectUnit(gridPos);
                    break;
                case GameState.UnitSelected:
                    TryCommandUnit(gridPos);
                    break;
                case GameState.AttackMode:
                    TryAttackUnit(gridPos);
                    break;
                case GameState.UnloadMode:
                    TryUnloadUnit(gridPos);
                    break;
            }
        }
        #region Unit controls
        void TrySelectUnit(Vector2 gridPos)
        {
            Collider2D[] hits = Physics2D.OverlapPointAll(gridPos);
            Debug.Log($"[Select] Found {hits.Length} colliders at {gridPos}");
            foreach (Collider2D hit in hits)
            {
                // Debug.Log($"  - Hit: '{hit.gameObject.name}' | Tag: '{hit.tag}'");

                if (hit.CompareTag("Unit"))
                {
                    UnitController unit = hit.GetComponent<UnitController>();
                    //Debug.Log($"  - isPlayer: {unit?.isPlayer} | CanAct: {unit?.CanAct()}");

                    if (unit != null && unit.isPlayer && unit.CanAct())
                    {
                        SelectUnit(unit);
                        return;
                    }
                }
            }
            foreach (Collider2D hit in hits)
            {
                UnitSpawner spawner = hit.GetComponent<UnitSpawner>();
                if (spawner != null && spawner.isPlayer)
                {
                    SelectSpawner(spawner);
                    return;
                }
            }
        }

        void SelectUnit(UnitController unit)
        {
            selectedUnit = unit;
            selectedUnit.SetSelected(true);
            currentPath.Clear();
            hasHoveredTile = false;

            // Moved earlier this turn but hasn't acted yet: go straight to its action menu
            if (unit.HasMoved)
            {
                reachableTiles.Clear();
                openMenu();
                return;
            }

            currentState = GameState.UnitSelected;
            reachableTiles = unit.GetReachableTiles(true);
            HighlightMoveRange(true);
            //Debug.Log("Selected: " + selectedUnit.name);
        }

        void HandleEdgeScroll()
        {
            if (!enableEdgeScroll || Camera.main == null || mouse == null) return;

            Vector2 mouseScreenPos = mouse.position.ReadValue();

            // Ignore if the cursor isn't actually within the game window
            if (mouseScreenPos.x < 0 || mouseScreenPos.x > Screen.width ||
                mouseScreenPos.y < 0 || mouseScreenPos.y > Screen.height)
                return;

            Vector3 moveDir = Vector3.zero;

            if (mouseScreenPos.x <= edgeScrollBorder) moveDir.x -= 1f;
            else if (mouseScreenPos.x >= Screen.width - edgeScrollBorder) moveDir.x += 1f;

            if (mouseScreenPos.y <= edgeScrollBorder) moveDir.y -= 1f;
            else if (mouseScreenPos.y >= Screen.height - edgeScrollBorder) moveDir.y += 1f;

            if (moveDir == Vector3.zero) return;

            Vector3 newPos = Camera.main.transform.position + moveDir.normalized * edgeScrollSpeed * Time.deltaTime;

            if (useCameraBounds)
            {
                newPos.x = Mathf.Clamp(newPos.x, cameraMinBounds.x, cameraMaxBounds.x);
                newPos.y = Mathf.Clamp(newPos.y, cameraMinBounds.y, cameraMaxBounds.y);
            }

            Camera.main.transform.position = newPos;
        }

        void TryCommandUnit(Vector2 gridPos)
        {
            Collider2D[] hits = Physics2D.OverlapPointAll(gridPos); // Clicked on the same unit -> deselect
            foreach (Collider2D hit in hits)
                if (hits != null && hit.gameObject == selectedUnit.gameObject)
                {
                    openMenu();
                    return;
                }
            foreach (Collider2D hit in hits)
                if (hits != null && hit.CompareTag("Unit")) //Clicked on another unit -> switch selection
                {
                    UnitController otherUnit = hit.GetComponent<UnitController>();

                    // An enemy we can't see counts as an empty tile, so moving there triggers an ambush
                    if (otherUnit != null && otherUnit.IsHiddenFrom(selectedUnit)) continue;

                    // Walking into a transport, or driving a transport onto a unit it can carry, loads them.
                    // Out of reach, it switches selection like any other friendly unit
                    if (otherUnit != null && selectedUnit.IsLoadPartner(otherUnit) && IsValidMove(gridPos))
                    {
                        MoveUnit(gridPos, otherUnit);
                        return;
                    }
                    DeselectUnit();
                    TrySelectUnit(gridPos);
                    return;
                }

            if (IsValidMove(gridPos)) // Clicked on empty tile -> try to move
            {
                MoveUnit(gridPos);
            }
            else
            {
                DeselectUnit(); // Clicked invalid tile
            }
        }
        bool IsValidMove(Vector2 gridPos)
        {
            // The path can pass through friendly units, but has to end on a tile the unit can stop on
            return currentPath.Count > 0 && currentPath[currentPath.Count - 1] == gridPos && reachableTiles.ContainsKey(gridPos);
        }

        // loadPartner: the unit on the destination tile to load with on arrival (null for a normal move)
        void MoveUnit(Vector2 gridPos, UnitController loadPartner = null)
        {
            HighlightMoveRange(false);
            isMoving = true;
            List<Vector2> pathToWalk = new List<Vector2>(currentPath);
            currentPath.Clear();

            UnitController mover = selectedUnit;
            mover.MoveAlongExplicitPath(pathToWalk, () =>
            {
                isMoving = false;
                if (mover.WasAmbushed || mover.IsDead)
                {
                    DeselectUnit(); // ambushed or killed by a trap: no action menu, no loading
                }
                else if (loadPartner != null && mover.CanLoadOnto(loadPartner))
                {
                    mover.LoadOnto(loadPartner); // walked into a transport: its turn is over
                    DeselectUnit();
                }
                else if (loadPartner != null && loadPartner.CanLoadOnto(mover))
                {
                    loadPartner.LoadOnto(mover); // transport picked the unit up and can still act
                    openMenu();
                }
                else openMenu();
            });
        }
    public void DeselectUnit()
    {
        if (selectedUnit != null)
        {
            HighlightMoveRange(false);
            selectedUnit.SetSelected(false);
            selectedUnit = null;
        }
        currentPath.Clear();
        currentState = GameState.Idle;
    }
    #endregion
    #region transport controls
    public void EnterUnloadMode()
    {
        if (selectedUnit == null || !CanUnloadSomewhere(selectedUnit)) return;

        HideActionMenu();
        PlayerUI.SetActive(false);
        currentState = GameState.UnloadMode;
        HighlightUnloadRange(selectedUnit, true);
    }
    void HighlightUnloadRange(UnitController transport, bool show)
    {
        TileFunction[] allTiles = FindObjectsByType<TileFunction>(FindObjectsSortMode.None);
        Vector2 transportPos = transport.transform.position;
        UnitController cargoUnit = transport.cargo[transport.cargo.Count - 1];

        foreach (TileFunction tile in allTiles)
        {
            Vector2 tilePos = tile.transform.position;
            float dist = Mathf.Abs(tilePos.x - transportPos.x) + Mathf.Abs(tilePos.y - transportPos.y);
            bool inRange = dist <= 1
                         && tile.CanUnitEnter(cargoUnit.unitData)
                         && !GridUtil.IsOccupied(tilePos);
            tile.SetHighlight(show && inRange);
        }
    }
    void TryUnloadUnit(Vector2 gridPos)
    {
        if (selectedUnit == null || selectedUnit.cargo.Count == 0)
        {
            currentState = GameState.Idle;
            return;
        }
        Vector2 transportPos = selectedUnit.transform.position;
        float dist = Mathf.Abs(gridPos.x - transportPos.x) + Mathf.Abs(gridPos.y - transportPos.y);
        UnitController cargoUnit = selectedUnit.cargo[selectedUnit.cargo.Count - 1];

        if (dist <= 1 && IsUnloadSpot(gridPos, cargoUnit))
        {
            selectedUnit.cargo.RemoveAt(selectedUnit.cargo.Count - 1);
            cargoUnit.UnloadTo(gridPos);
            HighlightUnloadRange(selectedUnit, false);
            selectedUnit.EndTurn(); // acting ends the transport's turn
            selectedUnit.SetSelected(false);
            selectedUnit = null;
            currentState = GameState.Idle;
        }
        else
        {
            Debug.Log("Invalid unload location");
        }
    }
    // A tile the cargo can be dropped on: one the cargo can enter, with no unit on it
    bool IsUnloadSpot(Vector2 gridPos, UnitController cargoUnit)
    {
        if (GridUtil.IsOccupied(gridPos)) return false;
        foreach (Collider2D hit in Physics2D.OverlapPointAll(gridPos))
        {
            TileFunction tile = hit.GetComponent<TileFunction>();
            if (tile != null && tile.CanUnitEnter(cargoUnit.unitData)) return true;
        }
        return false;
    }
    // True when the transport has cargo and at least one free tile next to it to drop it on
    bool CanUnloadSomewhere(UnitController transport)
    {
        if (transport.cargo.Count == 0) return false;
        UnitController cargoUnit = transport.cargo[transport.cargo.Count - 1];
        Vector2 pos = transport.transform.position;
        return IsUnloadSpot(pos + Vector2.up, cargoUnit) || IsUnloadSpot(pos + Vector2.down, cargoUnit)
            || IsUnloadSpot(pos + Vector2.left, cargoUnit) || IsUnloadSpot(pos + Vector2.right, cargoUnit);
    }
    #endregion
    #region Spawner controls
    void SelectSpawner(UnitSpawner spawner)
    {
        selectedSpawner = spawner;
        currentState = GameState.SpawnerSelected;
        OpenSpawnMenu();
    }
    void OpenSpawnMenu()
    {
        if (SpawnUI == null) return;
        SpawnUI.SetActive(true);
        ShowTabForSpawner();

        Vector2 screenPos = Camera.main.WorldToScreenPoint(selectedSpawner.transform.position);
        screenPos.y += 50f;
        SpawnUI.GetComponent<RectTransform>().position = screenPos;
    }

    // Opens the build menu on a tab this base can build from. A tab is the panel holding Create buttons, so a tab
    // fits when one of its buttons makes a unit this base builds. The tab left open last time stays if it fits
    // (a land base after building from Land); otherwise the first tab that fits is clicked, so the tab buttons'
    // own OnClick wiring shows it and hides the rest
    void ShowTabForSpawner()
    {
        if (spawnButtons == null) spawnButtons = IndexedButtons(SpawnUI, nameof(SpawnUnitAtSelected));

        Transform firstFit = null;
        foreach (var (button, index) in spawnButtons)
        {
            GameObject prefab = gm.GetPrefabAtIndex(index);
            UnitController unit = prefab != null ? prefab.GetComponent<UnitController>() : null;
            if (unit == null || !selectedSpawner.BuildsType(unit.unitData)) continue;

            Transform panel = button.transform.parent;
            if (panel.gameObject.activeSelf) return; // the open tab already fits
            if (firstFit == null) firstFit = panel;   // buttons come in menu order, so this is the topmost tab
        }
        if (firstFit == null) return;

        Button tab = firstFit.parent != null ? firstFit.parent.GetComponent<Button>() : null;
        if (tab != null) tab.onClick.Invoke();
        else firstFit.gameObject.SetActive(true);
    }
    public void CloseSpawnMenu()
    {
        if (SpawnUI != null) SpawnUI.SetActive(false);
        selectedSpawner = null;
        currentState = GameState.Idle;
    }

    public void SpawnUnitAtSelected(int index)
    {
        if (selectedSpawner == null) return;

        GameObject prefab = gm.GetPrefabAtIndex(index);
        if (prefab == null)
        {
            Debug.LogWarning($"No prefab at index {index} in GameManager's Prefab List (empty or Missing slot)");
            return;
        }

        // Check spawner type compatibility
        if (!selectedSpawner.CanSpawnUnit(prefab))
        {
            Debug.Log("This spawner cannot produce that unit type");
            return;
        }

        // Check tech tree unlock
        TechTree techTree = FindFirstObjectByType<TechTree>();
        if (techTree != null && !techTree.IsUnlocked(index, true))
        {
            Debug.Log("Unit not yet researched");
            return;
        }

        UnitTypeExtra data = prefab.GetComponent<UnitController>()?.unitData;
        if (data != null && !gm.CanAfford(data.price))
        {
            Debug.Log("Not enough funds");
            return;
        }

        selectedSpawner.SpawnUnit(prefab);
        if (data != null)
        {
            gm.AddMoney(-data.price);
        }
        CloseSpawnMenu();
    }
    #endregion
    #region Menu button looks
    // While the build or tech menu is open, buttons show what they'd do:
    //   normal = available, greyed out = not researched / can't afford / wrong kind of base / no research points,
    //   green = already researched. Each button's unit is the number in its own OnClick
    enum ButtonLook { Normal, Unavailable, Researched }
    static readonly Color ResearchedTint = new Color(0.55f, 0.95f, 0.55f);

    private List<(Button button, int index)> researchButtons;
    private List<(Button button, int index)> spawnButtons;
    private Dictionary<Button, (ColorBlock colors, Color graphic, Color text, ButtonLook look)> buttonDefaults
        = new Dictionary<Button, (ColorBlock, Color, Color, ButtonLook)>();

    void RefreshMenuButtons()
    {
        TechTree tech = TechTree.Instance;

        if (ResearchUI != null && ResearchUI.activeInHierarchy)
        {
            if (researchButtons == null) researchButtons = IndexedButtons(ResearchUI, nameof(ResearchUnit));
            foreach (var (button, index) in researchButtons)
            {
                bool researched = tech == null || tech.IsUnlocked(index, true);
                bool canResearch = !researched && tech.PlayerHasResearch() && gm.GetPrefabAtIndex(index) != null;
                SetLook(button, researched ? ButtonLook.Researched : canResearch ? ButtonLook.Normal : ButtonLook.Unavailable);
            }
        }

        if (SpawnUI != null && SpawnUI.activeInHierarchy && selectedSpawner != null)
        {
            if (spawnButtons == null) spawnButtons = IndexedButtons(SpawnUI, nameof(SpawnUnitAtSelected));
            foreach (var (button, index) in spawnButtons)
                SetLook(button, CanBuildHere(index, tech) ? ButtonLook.Normal : ButtonLook.Unavailable);
        }
    }

    // Researched, affordable, and the kind of unit the selected base makes
    bool CanBuildHere(int index, TechTree tech)
    {
        GameObject prefab = gm.GetPrefabAtIndex(index);
        UnitController unit = prefab != null ? prefab.GetComponent<UnitController>() : null;
        if (unit == null || unit.unitData == null) return false;
        if (tech != null && !tech.IsUnlocked(index, true)) return false;
        return gm.CanAfford(unit.unitData.price) && selectedSpawner.BuildsType(unit.unitData);
    }

    // Every button under the menu (including tabs not showing yet) that calls the method, with its OnClick number.
    // Upgrade buttons are left alone, since TechUpgradeButton handles those
    List<(Button, int)> IndexedButtons(GameObject menu, string methodName)
    {
        List<(Button, int)> result = new List<(Button, int)>();
        foreach (Button button in menu.GetComponentsInChildren<Button>(true))
        {
            if (button.GetComponent<TechUpgradeButton>() != null) continue;
            int index = ButtonOnClick.IntArgument(button, methodName);
            if (index >= 0) result.Add((button, index));
        }
        return result;
    }

    void SetLook(Button button, ButtonLook look)
    {
        TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
        if (!buttonDefaults.TryGetValue(button, out var d))
        {
            d = (button.colors, button.targetGraphic != null ? button.targetGraphic.color : Color.white,
                 label != null ? label.color : Color.white, ButtonLook.Normal);
        }
        else if (d.look == look) return; // nothing changed since last frame

        ColorBlock colors = d.colors;
        if (look == ButtonLook.Researched) colors.disabledColor = Color.white; // show the green at full strength
        button.colors = colors;
        button.interactable = look == ButtonLook.Normal;
        if (button.targetGraphic != null) button.targetGraphic.color = look == ButtonLook.Researched ? ResearchedTint : d.graphic;
        if (label != null) label.color = look == ButtonLook.Unavailable ? new Color(d.text.r, d.text.g, d.text.b, d.text.a * 0.45f) : d.text;

        d.look = look;
        buttonDefaults[button] = d;
    }
    #endregion
    #region Tech controls
    private bool researchOpenedFromMenu = false;

    // Called automatically by GameManager at the start of a new day
    public void OpenResearchMenu()
    {
        TechTree techTree = FindFirstObjectByType<TechTree>();
        Debug.Log($"[Research] Auto-open | techTree={(techTree == null ? "NULL" : "found")} | hasResearch={techTree?.PlayerHasResearch()}");
        if (techTree == null || !techTree.PlayerHasResearch()) return;

        researchOpenedFromMenu = false;
        currentState = GameState.ResearchOpen;
        if (ResearchUI != null) ResearchUI.SetActive(true);
    }

    // Wire the Tech button to THIS one instead — opens regardless of points remaining
    public void OpenResearchMenuFromButton()
    {
        TechTree techTree = FindFirstObjectByType<TechTree>();
        Debug.Log($"[Research] Button-open | techTree={(techTree == null ? "NULL" : "found")} | ResearchUI={(ResearchUI == null ? "NULL" : "assigned")}");

        if (techTree == null)
        {
            Debug.LogWarning("[Research] No TechTree component found in the scene");
            return;
        }
        if (ResearchUI == null)
        {
            Debug.LogWarning("[Research] ResearchUI is not assigned on the Cursor component");
            return;
        }

        researchOpenedFromMenu = (currentState == GameState.MenuOpen);
        PlayerUI.SetActive(false);
        currentState = GameState.ResearchOpen;
        ResearchUI.SetActive(true);
    }

    public void CloseResearchMenu()
    {
        if (ResearchUI != null) ResearchUI.SetActive(false);

        if (researchOpenedFromMenu && selectedUnit != null)
        {
            researchOpenedFromMenu = false;
            currentState = GameState.MenuOpen;
            ShowUnitMenu();
        }
        else
        {
            researchOpenedFromMenu = false;
            currentState = GameState.Idle;
        }
    }

    public void ResearchUnit(int index)
    {
        TechTree techTree = FindFirstObjectByType<TechTree>();
        Debug.Log($"[Research] Button clicked | index={index} | techTree={(techTree == null ? "NULL" : "found")} | hasResearch={techTree?.PlayerHasResearch()}");
        if (techTree == null) return;

        if (techTree.IsUnlocked(index, true))
        {
            Debug.Log("Already unlocked");
            return;
        }

        bool success = techTree.Unlock(index, true);
        Debug.Log($"[Research] Unlock({index}) returned {success}");
        if (success && !techTree.PlayerHasResearch())
        {
            CloseResearchMenu();
        }
    }
    #endregion
    #region Action Menu
    private bool actionMenuOpen = false;

    // Wired to the Action button: opens (or closes) a sub-menu of everything this unit can do
    public void EnterAttackMode()
    {
        if (selectedUnit == null) return;
        if (actionMenuOpen)
        {
            HideActionMenu();
            return;
        }

        List<(GameObject button, bool usable)> actions = GetUnitActions(selectedUnit);
        if (actions.RemoveAll(a => a.button == null) > 0)
            Debug.LogWarning("[Action] An action sub-menu button isn't assigned on the Cursor component");

        // Stack the unit's buttons top to bottom, centred on the Action button
        float y = (actions.Count - 1) * actionButtonSpacing / 2f;
        foreach (var (button, usable) in actions)
        {
            button.SetActive(true);
            button.GetComponent<Button>().interactable = usable;
            ((RectTransform)button.transform).anchoredPosition = new Vector2(actionMenuX, y);
            button.transform.localScale = Vector3.one; // Load/Unload and TrapPlace were saved at scale 0, so they showed as nothing
            y -= actionButtonSpacing;
        }
        actionMenuOpen = true;
    }

    // Every action the unit has, in sub-menu order. usable = false greys the button out
    List<(GameObject button, bool usable)> GetUnitActions(UnitController unit)
    {
        List<(GameObject button, bool usable)> actions = new List<(GameObject button, bool usable)>();
        UnitTypeExtra data = unit.unitData;
        if (data == null) return actions;

        if (unit.HasPrimaryWeapon) actions.Add((primaryAButton, unit.CanAttack() && unit.ammo > 0));
        if (unit.HasSecondaryWeapon) actions.Add((secondaryAButton, unit.CanAttack()));
        if (data.canTrap) actions.Add((trapButton, unit.CanUseTrapHere()));
        if (data.canTransport) actions.Add((unloadButton, CanUnloadSomewhere(unit)));
        return actions;
    }

    void HideActionMenu()
    {
        foreach (GameObject button in new[] { primaryAButton, secondaryAButton, trapButton, unloadButton })
            if (button != null) button.SetActive(false);
        actionMenuOpen = false;
    }

    // Shows the unit menu with the sub-menu closed, greying out Action if the unit can't do anything right now
    void ShowUnitMenu()
    {
        PlayerUI.SetActive(true);
        HideActionMenu();
        if (actionButton != null && selectedUnit != null)
            actionButton.interactable = GetUnitActions(selectedUnit).Exists(a => a.usable);
    }
    #endregion
    #region Attack Controls
    public void UsePrimaryAttack()
    {
        if (selectedUnit == null) return;
        selectedAttackType = UnitController.AttackType.Primary;
        HideActionMenu();
        PlayerUI.SetActive(false);
        currentState = GameState.AttackMode;
        HighlightAttackRange(selectedUnit, true);
    }

    public void UseSecondaryAttack()
    {
        if (selectedUnit == null) return;
        selectedAttackType = UnitController.AttackType.Secondary;
        HideActionMenu();
        PlayerUI.SetActive(false);
        currentState = GameState.AttackMode;
        HighlightAttackRange(selectedUnit, true);
    }

    void HighlightAttackRange(UnitController unit, bool show)
    {
        TileFunction[] allTiles = FindObjectsByType<TileFunction>(FindObjectsSortMode.None);
        Vector2 unitPos = unit.transform.position;

        foreach (TileFunction tile in allTiles)
        {
            Vector2 tilePos = tile.transform.position;
            float dist = Mathf.Abs(tilePos.x - unitPos.x) + Mathf.Abs(tilePos.y - unitPos.y);
            bool inRange = dist >= unit.GetAttackRangeMin() && dist <= unit.GetAttackRangeMax();
            tile.SetAttackHighlight(show && inRange);
        }
    }
    void TryAttackUnit(Vector2 gridPos)
    {

        Vector2 unitPos = selectedUnit.transform.position;
        float dist = Mathf.Abs(gridPos.x - unitPos.x) + Mathf.Abs(gridPos.y - unitPos.y);
        Debug.Log($"[Attack] Click {gridPos} | dist={dist} | range={selectedUnit.GetAttackRangeMin()}-{selectedUnit.GetAttackRangeMax()}");

        // Clicked outside attack range ?go back to menu
        if (dist < selectedUnit.GetAttackRangeMin() || dist > selectedUnit.GetAttackRangeMax())
        {
            HighlightAttackRange(selectedUnit, false);
            currentState = GameState.MenuOpen;
            ShowUnitMenu();
            return;
        }

        // Tile is in range ?check for any unit there (visible or not)
        Collider2D[] hits = Physics2D.OverlapPointAll(gridPos);
        bool hitTarget = false;

        foreach (Collider2D hit in hits)
        {
            if (!hit.CompareTag("Unit")) continue;

            UnitController target = hit.GetComponent<UnitController>();
            if (target == null) continue;
            bool canHit = selectedUnit.CanAttackTargetWith(target, selectedAttackType);
            Debug.Log($"[Attack] Target {target.name} | {selectedAttackType} can hit it={canHit}");
            if (!canHit)
            {
                // An enemy you can see that this weapon can't hit: back to the menu to pick the other weapon,
                // without using up the attack. Hidden units still swallow the shot, so a miss doesn't give them away
                if (target.IsVisibleToPlayer && target.isPlayer != selectedUnit.isPlayer)
                {
                    HighlightAttackRange(selectedUnit, false);
                    currentState = GameState.MenuOpen;
                    ShowUnitMenu();
                    return;
                }
                continue;
            }

            selectedUnit.PerformAttack(target, selectedAttackType);
            hitTarget = true;
            break;
        }

        if (!hitTarget)
        {
            // Attacked fog/empty tile ?still consumes the attack action
            selectedUnit.UseAttack();
            Debug.Log("Attacked tile ?no target found");
        }

        // Clean up either way. Acting ends the unit's turn, so it can't move afterwards
        HighlightAttackRange(selectedUnit, false);
        HighlightMoveRange(false);
        selectedUnit.EndTurn();
        selectedUnit.SetSelected(false);
        selectedUnit = null;
        currentState = GameState.Idle;

    }
    #endregion
    #region Menu buttons
    public void OnBack(InputAction.CallbackContext context)
    {
        if (!context.performed) return;

        if (currentState == GameState.ResearchOpen)
        {
            CloseResearchMenu();
            return;
        }

        if (currentState == GameState.ResearchOpen)
        {
            Debug.Log("Spend your research point before continuing");
        }

        if (currentState == GameState.SpawnerSelected)
        {
            CloseSpawnMenu();
        }
        else if (isUIOpen)
        {
            CloseMenu();
        }
        else if (currentState == GameState.UnitSelected)
        {
            DeselectUnit();
        }
    }

    // Closing the menu doesn't end the unit's turn: it keeps any action it hasn't used,
    // so it can be selected again later this turn
    void CloseMenu()
    {
        PlayerUI.SetActive(false);
        DeselectUnit();
    }

    public void openMenu()
    {
        if (currentState == GameState.MenuOpen) return;

        currentState = GameState.MenuOpen;
        ShowUnitMenu();
        // 마우스의 스크린 좌표를 UI의 월드 좌표에 그대로 적용
        Vector2 cursorScreenPos = Camera.main.WorldToScreenPoint(cursor.transform.position);
        Vector2 screenPos = cursorScreenPos;
        screenPos.y += 50.0f;
        Menu.position = screenPos;
        ClampMenuToScreen(cursorScreenPos);
    }
    void ClampMenuToScreen(Vector2 cursorScreenPos)
    {
        const float edgePanning = 50f;

        Canvas.ForceUpdateCanvases();

        Vector3[] corners = new Vector3[4];
        Menu.GetWorldCorners(corners);
        Vector3 pos = Menu.position;

        if (corners[1].y>Screen.height - edgePanning)
        {
            float topOffsetFromPivot = corners[1].y - pos.y;
            float newTop = cursorScreenPos.y - 200f;
            pos.y = newTop - topOffsetFromPivot;
            Menu.position = pos;

            Canvas.ForceUpdateCanvases();
            Menu.GetWorldCorners(corners);
        }

        if(corners[2].x>Screen.width - edgePanning)
        {
            pos.x -= (corners[2].x - Screen.width + edgePanning + 50f);
            Menu.position = pos;
        }
        else if (corners[0].x < edgePanning)
        {
            pos.x -= (corners[0].x - edgePanning -50f);
            Menu.position = pos;
        }
    }

    public void EndTurnButton()
    {
        // Ignore presses during the enemy turn, or while a unit is still walking or an ability is playing
        if (!gm.IsPlayerTurn || isMoving || Class.AbilityPlaying) return;

        // Close all open UI
        PlayerUI.SetActive(false);
        if (SpawnUI != null) SpawnUI.SetActive(false);

        // Clean up any selected unit
        if (selectedUnit != null)
        {
            HighlightMoveRange(false);
            HighlightAttackRange(selectedUnit, false);
            selectedUnit.SetSelected(false);
            selectedUnit = null;
        }

        selectedSpawner = null;
        currentState = GameState.Idle;

        // Now end the turn
        gm.TurnEnd();
    }
    #endregion
    public void UseTrapAbilityButton()
    {
        if (selectedUnit == null) return;
        if (!selectedUnit.CanUseTrapHere())
        {
            return;
        }

        selectedUnit.UseTrapAbility();

        HighlightMoveRange(false);
        selectedUnit.EndTurn(); // acting ends the unit's turn
        selectedUnit.SetSelected(false);
        selectedUnit = null;
        HideActionMenu();
        PlayerUI.SetActive(false);
        currentState = GameState.Idle;
    }
    void HighlightMoveRange(bool show)
    {
        TileFunction[] allTiles = FindObjectsByType<TileFunction>(FindObjectsSortMode.None);
        foreach (TileFunction tile in allTiles)
        {
            Vector2 tilePos = tile.transform.position;
            bool inRange = reachableTiles.ContainsKey(tilePos);
            tile.SetHighlight(show && inRange);
        }
    }
    void UpdateHoverPath()
    {
        Vector2 mouseScreenPos = mouse.position.ReadValue();
        Vector2 mouseWorldPos = Camera.main.ScreenToWorldPoint(mouseScreenPos);
        Vector2 gridPos = new Vector2(Mathf.Round(mouseWorldPos.x), Mathf.Round(mouseWorldPos.y));

        if (hasHoveredTile && gridPos == lastHoveredTile) return;
        Vector2 previous = hasHoveredTile ? lastHoveredTile : (Vector2)selectedUnit.transform.position;
        hasHoveredTile = true;
        lastHoveredTile = gridPos;

        // A fast mouse move can skip tiles in one frame, so replay it one tile at a time
        foreach (Vector2 tile in GridSteps(previous, gridPos))
            TraceStep(tile);
        RefreshPathVisual();
    }

    // Adds the tile the cursor just entered to the drawn path, like drawing the move arrow in Advance Wars
    void TraceStep(Vector2 tile)
    {
        Vector2 unitPos = selectedUnit.transform.position;
        if (tile == unitPos)
        {
            currentPath.Clear();
            return;
        }

        // Moving back over the path cuts it back to that tile
        int index = currentPath.IndexOf(tile);
        if (index >= 0)
        {
            currentPath.RemoveRange(index + 1, currentPath.Count - index - 1);
            return;
        }

        // Extend the path if the tile is next to its end, within move range, and passable
        Vector2 end = currentPath.Count > 0 ? currentPath[currentPath.Count - 1] : unitPos;
        if (GridUtil.Manhattan(end, tile) == 1 && currentPath.Count < selectedUnit.GetMoveBudget() && selectedUnit.CanPassThrough(tile))
        {
            currentPath.Add(tile);
            return;
        }

        // Too long, or the cursor cut across ground the unit can't cross: redraw the shortest route.
        // If the tile can't be reached at all, keep the path as it is.
        if (reachableTiles.ContainsKey(tile))
            currentPath = selectedUnit.FindPath(tile) ?? new List<Vector2>();
    }

    // Tiles crossed going from one tile to another in single up/down/left/right steps (start excluded)
    List<Vector2> GridSteps(Vector2 from, Vector2 to)
    {
        List<Vector2> tiles = new List<Vector2>();
        int dx = Mathf.RoundToInt(Mathf.Abs(to.x - from.x));
        int dy = Mathf.RoundToInt(Mathf.Abs(to.y - from.y));
        Vector2 stepX = new Vector2(Mathf.Sign(to.x - from.x), 0);
        Vector2 stepY = new Vector2(0, Mathf.Sign(to.y - from.y));

        Vector2 pos = from;
        for (int ix = 0, iy = 0; ix < dx || iy < dy;)
        {
            // Step along whichever axis is further behind the straight line between the two tiles
            if ((0.5f + ix) / dx < (0.5f + iy) / dy) { pos += stepX; ix++; }
            else { pos += stepY; iy++; }
            tiles.Add(pos);
        }
        return tiles;
    }
    void RefreshPathVisual()
    {
        HighlightMoveRange(true);
        foreach (Vector2 pos in currentPath)
        {
            TileFunction tile = GridUtil.TileAt(pos);
            if (tile != null) tile.SetPathHighlight(true);
        }
    }
}
