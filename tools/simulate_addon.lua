-- Runs the real addon code against a minimal fake WoW API and prints the strip's
-- block values after each simulated action. The C# tests use this output to prove the
-- addon and the companion app agree on the protocol.
--
-- Usage: lua5.1 tools/simulate_addon.lua addon/PixelTunes > companion/tests/PixelTunes.Tests/addon_vectors.txt

local addonDir = arg[1] or "addon/PixelTunes"
local now = 100.0
local timers = {}
local onUpdates = {}

local function noop() end

-- Generic fake widget: any unknown method is a no-op returning nil.
local Widget = {}
function Widget.new(kind)
  local w = { kind = kind, shown = true, scripts = {}, color = nil, children = {}, effScale = 1 }
  return setmetatable(w, Widget)
end
Widget.__index = function(t, k)
  local v = rawget(Widget, k)
  if v then return v end
  return noop
end
function Widget:CreateTexture() local t = Widget.new("Texture"); table.insert(self.children, t); return t end
function Widget:CreateFontString() return Widget.new("FontString") end
function Widget:SetColorTexture(r, g, b) self.color = { r, g, b } end
function Widget:SetScript(name, fn)
  self.scripts[name] = fn
  if name == "OnUpdate" then onUpdates[self] = fn end
end
function Widget:Show() self.shown = true end
function Widget:Hide() self.shown = false end
function Widget:SetShown(v) self.shown = not not v end
function Widget:IsShown() return self.shown end
function Widget:GetEffectiveScale() return self.effScale end
function Widget:GetPoint() return "CENTER", nil, "CENTER", 0, 0 end

local frames = {}
function CreateFrame(kind, name)
  local f = Widget.new(kind)
  if name then _G[name] = f end
  table.insert(frames, f)
  return f
end
UIParent = Widget.new("Frame")
GameTooltip = Widget.new("GameTooltip")
DEFAULT_CHAT_FRAME = { AddMessage = function(_, msg) io.stderr:write("[chat] ", msg, "\n") end }
BackdropTemplateMixin = {}
SlashCmdList = {}
function GetTime() return now end
function GetPhysicalScreenSize() return 2560, 1440 end
C_Timer = { After = function(d, fn) table.insert(timers, { at = now + d, fn = fn }) end }
function strtrim(s) return (s:gsub("^%s+", ""):gsub("%s+$", "")) end
function strsplit(sep, s, n)
  local a, b = s:match("^(.-)" .. sep .. "(.*)$")
  if a then return a, b end
  return s
end

-- Load the addon files listed in the .toc, sharing one private namespace table.
local ns = {}
local toc = assert(io.open(addonDir .. "/PixelTunes.toc")):read("*a")
for file in toc:gmatch("[^\r\n]+") do
  if not file:match("^##") and file:match("%.lua$") then
    local chunk = assert(loadfile(addonDir .. "/" .. file))
    chunk("PixelTunes", ns)
  end
end

local strip = _G.PixelTunesStrip
local function Fire(event, a1)
  for _, f in ipairs(frames) do
    if f.scripts.OnEvent then f.scripts.OnEvent(f, event, a1) end
  end
end

local function Advance(dt)
  local target = now + dt
  while now < target do
    now = math.min(target, now + 0.016)
    for f, fn in pairs(onUpdates) do
      if f.scripts.OnUpdate == fn then fn(f, 0.016) else onUpdates[f] = nil end
    end
  end
end

local function Snapshot(label)
  local vals = {}
  for i, tex in ipairs(strip.children) do
    local c = tex.color
    vals[i] = c[1] * 4 + c[2] * 2 + c[3]
  end
  print(label .. " " .. table.concat(vals, ","))
end

Fire("ADDON_LOADED", "PixelTunes")
Fire("PLAYER_LOGIN")
Snapshot("idle")

PixelTunes_Send("next");      Snapshot("next1")
Advance(0.05)
PixelTunes_Send("next")       -- too soon: queued
Snapshot("next2_queued")
Advance(0.3);                 Snapshot("next2_flushed")
SlashCmdList.PIXELTUNES("play"); Snapshot("playpause")
Advance(0.3)
SlashCmdList.PIXELTUNES("prev"); Snapshot("prev")
Advance(0.3)
SlashCmdList.PIXELTUNES("vol+"); Snapshot("volup")
Advance(0.3)
PixelTunes_Send("voldown"); Snapshot("voldown")
Advance(0.05)
PixelTunes_Send("voldown"); Snapshot("voldown2_queued")
Advance(0.3); Snapshot("voldown2_flushed")
-- Wrap the 6-bit sequence counter.
for i = 1, 70 do
  Advance(0.25)
  local cycle = { "next", "prev", "volup", "voldown", "playpause" }
  PixelTunes_Send(cycle[(i % #cycle) + 1])
  Snapshot("cycle" .. i)
end
