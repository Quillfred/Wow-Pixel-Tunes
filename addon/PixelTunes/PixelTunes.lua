-- PixelTunes: control Spotify (or any Windows media player) from inside WoW.
--
-- WoW addons cannot talk to other programs. This addon only draws a tiny strip of
-- colored squares in the top-left corner of the screen. The PixelTunes companion app
-- on your PC reads those colors and presses play/next/previous for you through the
-- Windows media controls. Nothing is ever sent back into WoW.

local ADDON_NAME, ns = ...
local P = ns.Protocol

local DEFAULTS = {
  blockSize = 4,      -- size of each strip block, in physical screen pixels
  panelShown = true,
  locked = false,
  stripShown = true,
  scale = 1,          -- panel size multiplier (/ptunes scale)
  point = nil,        -- saved panel position
}
local MIN_INTERVAL = 0.2 -- seconds between strip changes, so the companion sees every press
local MAX_QUEUE = 5

local db
local seq = 0
local lastChange = -math.huge
local queue = {}

local function Print(msg)
  DEFAULT_CHAT_FRAME:AddMessage("|cff1ed760PixelTunes|r: " .. msg)
end

------------------------------------------------------------------------------------
-- The pixel strip
------------------------------------------------------------------------------------

-- Parentless, so it stays visible when the UI is hidden with Alt-Z.
local strip = CreateFrame("Frame", "PixelTunesStrip", nil)
strip:SetFrameStrata("TOOLTIP")
strip:SetFrameLevel(9000)
strip:EnableMouse(false)

local blocks = {}
for i = 1, P.BLOCKS do
  local tex = strip:CreateTexture(nil, "OVERLAY")
  if tex.SetSnapToPixelGrid then
    tex:SetSnapToPixelGrid(true)
  end
  blocks[i] = tex
end

-- Converts physical screen pixels to UI units for the strip.
local function PixelsToUnits(px)
  local physH = GetPhysicalScreenSize and select(2, GetPhysicalScreenSize())
  if not physH or physH <= 0 then
    return px
  end
  return px * (768 / physH) / strip:GetEffectiveScale()
end

local function LayoutStrip()
  local size = PixelsToUnits(db and db.blockSize or DEFAULTS.blockSize)
  strip:ClearAllPoints()
  strip:SetPoint("TOPLEFT", UIParent, "TOPLEFT", 0, 0)
  strip:SetSize(size * P.BLOCKS, size)
  for i, tex in ipairs(blocks) do
    tex:ClearAllPoints()
    tex:SetSize(size, size)
    tex:SetPoint("TOPLEFT", strip, "TOPLEFT", (i - 1) * size, 0)
  end
end

local function Draw(cmd)
  local values = P.Encode(cmd, seq)
  for i, tex in ipairs(blocks) do
    local r, g, b = P.Color(values[i])
    tex:SetColorTexture(r, g, b, 1)
  end
end

local function Apply(cmd)
  seq = (seq + 1) % P.SEQ_MOD
  Draw(cmd)
  lastChange = GetTime()
end

local function FlushQueue(self)
  if #queue == 0 then
    self:SetScript("OnUpdate", nil)
    return
  end
  if GetTime() - lastChange >= MIN_INTERVAL then
    Apply(table.remove(queue, 1))
  end
end

------------------------------------------------------------------------------------
-- Control panel
------------------------------------------------------------------------------------

local BUTTON_HEIGHT = 18
local PAD = 5

local panel = CreateFrame("Frame", "PixelTunesPanel", UIParent, BackdropTemplateMixin and "BackdropTemplate" or nil)
panel:SetSize(200, BUTTON_HEIGHT + 2 * PAD)
panel:SetPoint("CENTER", UIParent, "CENTER", 0, -200)
panel:SetFrameStrata("MEDIUM")
panel:SetClampedToScreen(true)
panel:EnableMouse(true)
panel:SetMovable(true)
panel:RegisterForDrag("LeftButton")
if panel.SetBackdrop then
  panel:SetBackdrop({
    bgFile = "Interface\\Tooltips\\UI-Tooltip-Background",
    edgeFile = "Interface\\Tooltips\\UI-Tooltip-Border",
    tile = true, tileSize = 16, edgeSize = 10,
    insets = { left = 2, right = 2, top = 2, bottom = 2 },
  })
  panel:SetBackdropColor(0, 0, 0, 0.6)
  panel:SetBackdropBorderColor(0.35, 0.35, 0.35, 1)
end
panel:SetScript("OnEnter", function(self)
  GameTooltip:SetOwner(self, "ANCHOR_TOP")
  GameTooltip:SetText("|cff1ed760PixelTunes|r", 1, 1, 1)
  GameTooltip:AddLine(db and db.locked and "Locked (/ptunes unlock to move)" or "Drag to move", 0.7, 0.7, 0.7)
  GameTooltip:AddLine("/ptunes help for options", 0.7, 0.7, 0.7)
  GameTooltip:Show()
end)
panel:SetScript("OnLeave", function()
  GameTooltip:Hide()
end)

-- Brief feedback ("Next", "Volume up", ...) shown just above the panel.
local status = panel:CreateFontString(nil, "OVERLAY", "GameFontHighlightSmall")
status:SetPoint("BOTTOM", panel, "TOP", 0, 2)
status:SetText("")

local statusToken = 0
local function ShowStatus(text)
  status:SetText(text)
  statusToken = statusToken + 1
  local token = statusToken
  if C_Timer and C_Timer.After then
    C_Timer.After(1.5, function()
      if token == statusToken then
        status:SetText("")
      end
    end)
  end
end

panel:SetScript("OnDragStart", function(self)
  if not db.locked then
    self:StartMoving()
  end
end)
panel:SetScript("OnDragStop", function(self)
  self:StopMovingOrSizing()
  local point, _, relPoint, x, y = self:GetPoint()
  db.point = { point, relPoint, x, y }
end)

-- Sends a command ("playpause", "next", "prev", "volup" or "voldown"). Global so keybinds and macros can use it.
function PixelTunes_Send(name)
  local cmd = P.Commands[name]
  if not cmd then
    Print("unknown command '" .. tostring(name) .. "'")
    return
  end
  if not strip:IsShown() then
    Print("the pixel strip is hidden, so the companion app can't see commands. Type /ptunes strip to show it.")
  end
  if #queue == 0 and GetTime() - lastChange >= MIN_INTERVAL then
    Apply(cmd)
  elseif #queue < MAX_QUEUE then
    queue[#queue + 1] = cmd
    strip:SetScript("OnUpdate", FlushQueue)
  end
  ShowStatus(P.CommandNames[cmd])
end

local function MakeButton(label, width, command, tooltip, anchorTo, x)
  local b = CreateFrame("Button", nil, panel, "UIPanelButtonTemplate")
  b:SetSize(width, BUTTON_HEIGHT)
  b:SetText(label)
  b:SetNormalFontObject("GameFontNormalSmall")
  b:SetHighlightFontObject("GameFontHighlightSmall")
  if anchorTo then
    b:SetPoint("LEFT", anchorTo, "RIGHT", x, 0)
  else
    b:SetPoint("LEFT", panel, "LEFT", PAD, 0)
  end
  b:SetScript("OnClick", function()
    PixelTunes_Send(command)
  end)
  b:SetScript("OnEnter", function(self)
    GameTooltip:SetOwner(self, "ANCHOR_TOP")
    GameTooltip:SetText(tooltip, 1, 1, 1)
    GameTooltip:Show()
  end)
  b:SetScript("OnLeave", function()
    GameTooltip:Hide()
  end)
  return b
end

local VOLUME_TIP = "\n|cffaaaaaaChanges the player's volume, not WoW's|r"
local prevButton = MakeButton("<<", 24, "prev", "Previous track")
local playButton = MakeButton("Play / Pause", 70, "playpause", "Play / Pause", prevButton, 2)
local nextButton = MakeButton(">>", 24, "next", "Next track", playButton, 2)
local volLabel = panel:CreateFontString(nil, "OVERLAY", "GameFontNormalSmall")
volLabel:SetPoint("LEFT", nextButton, "RIGHT", 8, 0)
volLabel:SetText("Vol")
local volDownButton = MakeButton("-", 18, "voldown", "Volume down" .. VOLUME_TIP, volLabel, 4)
MakeButton("+", 18, "volup", "Volume up" .. VOLUME_TIP, volDownButton, 2)
-- Fit the panel around the buttons (the label width can read as 0 before fonts load).
local labelWidth = volLabel:GetStringWidth() or 0
if labelWidth < 1 then labelWidth = 18 end
panel:SetWidth(PAD + 24 + 2 + 70 + 2 + 24 + 8 + math.ceil(labelWidth) + 4 + 18 + 2 + 18 + PAD)

------------------------------------------------------------------------------------
-- Keybinds (Esc > Options > Keybindings > AddOns > PixelTunes)
------------------------------------------------------------------------------------

BINDING_HEADER_PIXELTUNES = "PixelTunes"
BINDING_NAME_PIXELTUNES_PLAYPAUSE = "Play / Pause"
BINDING_NAME_PIXELTUNES_NEXT = "Next track"
BINDING_NAME_PIXELTUNES_PREV = "Previous track"
BINDING_NAME_PIXELTUNES_VOLUP = "Volume up"
BINDING_NAME_PIXELTUNES_VOLDOWN = "Volume down"
BINDING_NAME_PIXELTUNES_TOGGLE = "Show / hide panel"

------------------------------------------------------------------------------------
-- Settings, events, slash commands
------------------------------------------------------------------------------------

local function ApplySettings()
  LayoutStrip()
  strip:SetShown(db.stripShown)
  panel:SetShown(db.panelShown)
  panel:SetScale(db.scale)
  if db.point then
    panel:ClearAllPoints()
    panel:SetPoint(db.point[1], UIParent, db.point[2], db.point[3], db.point[4])
  end
end

function PixelTunes_TogglePanel()
  db.panelShown = not panel:IsShown()
  panel:SetShown(db.panelShown)
end

local events = CreateFrame("Frame")
events:RegisterEvent("ADDON_LOADED")
events:RegisterEvent("PLAYER_LOGIN")
events:RegisterEvent("DISPLAY_SIZE_CHANGED")
events:RegisterEvent("UI_SCALE_CHANGED")
events:RegisterEvent("SCREENSHOT_STARTED")
events:RegisterEvent("SCREENSHOT_SUCCEEDED")
events:RegisterEvent("SCREENSHOT_FAILED")
events:SetScript("OnEvent", function(_, event, arg1)
  if event == "ADDON_LOADED" and arg1 == ADDON_NAME then
    PixelTunesDB = PixelTunesDB or {}
    db = PixelTunesDB
    for k, v in pairs(DEFAULTS) do
      if db[k] == nil then
        db[k] = v
      end
    end
    Draw(0) -- idle frame: sequence 0, no command
    ApplySettings()
  elseif event == "PLAYER_LOGIN" or event == "DISPLAY_SIZE_CHANGED" or event == "UI_SCALE_CHANGED" then
    if db then
      LayoutStrip()
    end
  elseif event == "SCREENSHOT_STARTED" then
    -- Keep the strip out of your screenshots.
    strip:SetAlpha(0)
  elseif event == "SCREENSHOT_SUCCEEDED" or event == "SCREENSHOT_FAILED" then
    strip:SetAlpha(1)
  end
end)

local HELP = {
  "/ptunes - show or hide the panel",
  "/ptunes next | prev | play - send a command (works in macros too)",
  "/ptunes vol+ | vol- - turn the player's volume up or down",
  "/ptunes lock | unlock - lock the panel in place",
  "/ptunes reset - move the panel back to the middle of the screen",
  "/ptunes scale <0.5-2> - make the panel smaller or bigger (default 1)",
  "/ptunes size <1-16> - strip block size in pixels (default 4)",
  "/ptunes strip - show or hide the pixel strip",
  "/ptunes status - show diagnostic info",
}

SLASH_PIXELTUNES1 = "/ptunes"
SLASH_PIXELTUNES2 = "/pixeltunes"
SlashCmdList.PIXELTUNES = function(msg)
  local cmd, arg = strsplit(" ", strtrim((msg or ""):lower()), 2)
  if cmd == "" or cmd == "toggle" then
    PixelTunes_TogglePanel()
  elseif cmd == "next" then
    PixelTunes_Send("next")
  elseif cmd == "prev" or cmd == "previous" then
    PixelTunes_Send("prev")
  elseif cmd == "play" or cmd == "pause" or cmd == "playpause" then
    PixelTunes_Send("playpause")
  elseif cmd == "vol+" or cmd == "volup" then
    PixelTunes_Send("volup")
  elseif cmd == "vol-" or cmd == "voldown" then
    PixelTunes_Send("voldown")
  elseif cmd == "lock" or cmd == "unlock" then
    db.locked = (cmd == "lock")
    Print(db.locked and "panel locked." or "panel unlocked, drag it to move.")
  elseif cmd == "reset" then
    db.point = nil
    panel:ClearAllPoints()
    panel:SetPoint("CENTER", UIParent, "CENTER", 0, -200)
    db.panelShown = true
    panel:Show()
  elseif cmd == "scale" then
    local n = tonumber(arg)
    if n and n >= 0.5 and n <= 2 then
      db.scale = n
      panel:SetScale(n)
      Print("panel scale set to " .. n .. ".")
    else
      Print("usage: /ptunes scale <0.5-2>  (current: " .. db.scale .. ")")
    end
  elseif cmd == "size" then
    local n = tonumber(arg)
    if n and n >= 1 and n <= 16 then
      db.blockSize = math.floor(n)
      LayoutStrip()
      Print("strip block size set to " .. db.blockSize .. " px.")
    else
      Print("usage: /ptunes size <1-16>  (current: " .. db.blockSize .. ")")
    end
  elseif cmd == "strip" then
    db.stripShown = not db.stripShown
    strip:SetShown(db.stripShown)
    Print(db.stripShown and "pixel strip shown." or "pixel strip hidden. The companion app can't see commands until you show it again.")
  elseif cmd == "status" then
    local w, h = 0, 0
    if GetPhysicalScreenSize then
      w, h = GetPhysicalScreenSize()
    end
    Print(string.format("screen %dx%d, block %d px, strip %s, sequence %d, queued %d",
      w, h, db.blockSize, strip:IsShown() and "shown" or "hidden", seq, #queue))
  else
    for _, line in ipairs(HELP) do
      Print(line)
    end
  end
end
