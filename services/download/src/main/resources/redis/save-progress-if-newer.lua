local current = redis.call('HGET', KEYS[1], 'clientTs')

if current and tonumber(current) >= tonumber(ARGV[4]) then
  redis.call('PEXPIRE', KEYS[1], ARGV[5])
  return 0
end

redis.call('HSET', KEYS[1],
  'positionSec', ARGV[1],
  'durationSec', ARGV[2],
  'status', ARGV[3],
  'clientTs', ARGV[4])
redis.call('PEXPIRE', KEYS[1], ARGV[5])
return 1
