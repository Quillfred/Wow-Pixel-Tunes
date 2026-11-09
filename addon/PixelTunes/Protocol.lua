-- PixelTunes pixel protocol. Must stay in sync with
-- companion/src/PixelTunes.Core/Protocol.cs.
--
-- The strip is 7 square blocks. Each block is a solid color whose channels are either
-- fully off or fully on, so it carries 3 bits: value = R*4 + G*2 + B.
--
--   block 1  start marker  5 (magenta)
--   block 2  start marker  3 (cyan)
--   block 3  command       0 none, 1 play/pause, 2 next, 3 previous,
--                          4 volume up, 5 volume down
--   block 4  sequence high floor(seq / 8)
--   block 5  sequence low  seq % 8
--   block 6  checksum      (cmd + 3*hi + 5*lo + 2) % 8
--   block 7  end marker    6 (yellow)

local _, ns = ...
ns = ns or {}

local P = {}
ns.Protocol = P

P.BLOCKS = 7
P.MARK_A, P.MARK_B, P.MARK_END = 5, 3, 6
P.SEQ_MOD = 64
P.Commands = { playpause = 1, next = 2, prev = 3, volup = 4, voldown = 5 }
P.CommandNames = { [1] = "Play / Pause", [2] = "Next", [3] = "Previous", [4] = "Volume up", [5] = "Volume down" }

function P.Checksum(cmd, hi, lo)
  return (cmd + 3 * hi + 5 * lo + 2) % 8
end

-- Returns the 7 block values for a command and sequence number.
function P.Encode(cmd, seq)
  local hi = math.floor(seq / 8) % 8
  local lo = seq % 8
  return { P.MARK_A, P.MARK_B, cmd, hi, lo, P.Checksum(cmd, hi, lo), P.MARK_END }
end

-- Returns r, g, b (each 0 or 1) for a 3-bit block value.
function P.Color(value)
  return math.floor(value / 4) % 2, math.floor(value / 2) % 2, value % 2
end

return P
