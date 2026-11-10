class_name TapFunClient
extends Node
## TapFun client API and Web API for games running on TapFun cloud gaming (Tencent Cloud CAR).
##
## Add this node to your scene (or register the script as an autoload). It reads the
## launch arguments in _ready(). Call listen() once, then send() requests built with
## the static functions below. Answers from TapFun arrive through message_received.
##
## Uses only APIs available since Godot 4.2.

## Emitted for each message from TapFun, with the query string decoded into a Dictionary.
## Answers have a "from" key (for example from=getPlayerEnvironment). Notifications that
## TapFun sends on its own have req=clientStatus.
signal message_received(message: Dictionary)

## Emitted when the answer of inputTextMultiLine is complete. TapFun sends the entered text
## in 128-character chunks; this joins them. result is "submit" or "cancel".
signal input_text_multi_line_received(label: String, result: String, text: String)

## Emitted when a request finishes. status is the HTTP status, or 0 if the connection failed.
signal request_completed(req: String, status: int, body: String)

## Answers to requests sent sooner than this (seconds) after the game starts can be lost.
## Wait this long after the game starts before sending a request whose answer you need.
const STARTUP_WAIT_SEC := 5.0

## How long to wait for an answer before sending the request again (seconds). Send again only
## requests that show nothing to the player, such as getPlayerEnvironment (fetch_player_environment()).
const RETRY_INTERVAL_SEC := 3.0

## Your app name and version. A User-Agent header is required: requests without one are rejected with 403.
@export var user_agent := "TapFunSampleGodot/1.0"

## All options from the command line: "--name" -> "value".
var args := {}
## TapFun user ID (--tapfun-user-id). Not secret, but do not show it on screen.
var user_id := ""
## Host name of the TapFun API (--api-host), for example tapfun.co.jp.
var api_host := ""
## Bearer token for the client API and the Web API (--bearer-token).
var bearer_token := ""
## UDP port to receive messages from TapFun (--port). Usually 10100.
var port := 10100
## The player's most preferred browser language (--lang), a BCP 47 tag such as "ja" or "en-US".
## Empty when it is not passed or TapFun could not get it. Not in the default launch parameters:
## add --lang {lang} --country {country}.
var language := ""
## The country the player connects from (--country), such as "JP".
## Empty when it is not passed or TapFun could not determine it.
var country := ""

var _udp := PacketPeerUDP.new()
var _multi_line_chunks := {}  # label -> {seq: text}


func _ready() -> void:
	# Keep receiving while the game is paused.
	process_mode = Node.PROCESS_MODE_ALWAYS
	# Options the engine does not use stay in get_cmdline_args(). Options written after "--"
	# are only in get_cmdline_user_args(), so read both.
	args = parse_args(OS.get_cmdline_args() + OS.get_cmdline_user_args())
	user_id = args.get("--tapfun-user-id", "")
	api_host = args.get("--api-host", "")
	bearer_token = args.get("--bearer-token", "")
	port = int(args.get("--port", "10100"))
	language = args.get("--lang", "")
	country = args.get("--country", "")


## true when the values needed to call the API are present.
func has_client_api_values() -> bool:
	return user_id != "" and api_host != "" and bearer_token != ""


## Starts listening on 127.0.0.1:port.
func listen() -> Error:
	return _udp.bind(port, "127.0.0.1")


func _process(_delta: float) -> void:
	if not _udp.is_bound():
		return
	while _udp.get_available_packet_count() > 0:
		var packet := _udp.get_packet()
		# Reply "OK" to each message.
		_udp.set_dest_address(_udp.get_packet_ip(), _udp.get_packet_port())
		_udp.put_packet("OK".to_utf8_buffer())
		var message := decode_query(packet.get_string_from_utf8())
		message_received.emit(message)
		if message.get("from", "") == "inputTextMultiLine":
			_add_multi_line_chunk(message)


# UDP does not guarantee the order, so the chunks are put together by seq.
func _add_multi_line_chunk(message: Dictionary) -> void:
	var label: String = message.get("label", "")
	var result: String = message.get("result", "")
	if result != "submit":
		_multi_line_chunks.erase(label)
		input_text_multi_line_received.emit(label, result, "")
		return
	var end := int(message.get("end", "0"))
	if not _multi_line_chunks.has(label):
		_multi_line_chunks[label] = {}
	var parts: Dictionary = _multi_line_chunks[label]
	parts[int(message.get("seq", "0"))] = message.get("text", "")
	if parts.size() < end:
		return
	var text := ""
	for seq in range(1, end + 1):
		text += parts.get(seq, "")
	_multi_line_chunks.erase(label)
	input_text_multi_line_received.emit(label, result, text)


# --- Client API ---

## Sends one client API request: POST /api/v1/custom_event?userId={user_id}.
## TapFun forwards it to the player's browser.
## Returns {"status": HTTP status or 0 if the connection failed, "body": String}.
## Use await when the next request must wait for this one; otherwise just call it.
func send(request: Dictionary) -> Dictionary:
	var url := "%s/custom_event?userId=%s" % [_base_url(), user_id.uri_encode()]
	var result := await _http(HTTPClient.METHOD_POST, url, "application/x-www-form-urlencoded", encode_query(request).to_utf8_buffer())
	request_completed.emit(str(request.get("req", "")), result.status, result.body)
	return result


## Sends getPlayerEnvironment and returns the answer, or {} when none arrived. The answer can be
## lost when the request is sent soon after the game starts (see STARTUP_WAIT_SEC), so when no
## answer arrives within retry_interval seconds, this sends the request again, up to attempts times.
## Call listen() first. The answer is also emitted through message_received.
func fetch_player_environment(attempts := 5, retry_interval := RETRY_INTERVAL_SEC) -> Dictionary:
	if not _udp.is_bound():
		push_error("Call listen() first.")
		return {}
	var answer := {}
	var on_message := func(message: Dictionary) -> void:
		if message.get("from", "") == "getPlayerEnvironment" and answer.is_empty():
			answer.merge(message)
	message_received.connect(on_message)
	for i in attempts:
		await send(get_player_environment())
		var deadline := Time.get_ticks_msec() + int(retry_interval * 1000)
		while answer.is_empty() and Time.get_ticks_msec() < deadline:
			await get_tree().process_frame
		if not answer.is_empty():
			break
	message_received.disconnect(on_message)
	return answer


## Splits a long initial text for input_text_multi_line() into inputTextMultiLineChunkText
## requests (seq 1, 2, ... up to 100). Send them all, then send input_text_multi_line()
## with "end" = the number of chunks.
static func input_text_multi_line_chunks(text: String, chunk_length := 128) -> Array[Dictionary]:
	var chunks: Array[Dictionary] = []
	var seq := 1
	for start in range(0, text.length(), chunk_length):
		chunks.append({"req": "inputTextMultiLineChunkText", "text": text.substr(start, chunk_length), "seq": str(seq)})
		seq += 1
	return chunks


# Requests. The data passes through the player's browser, so do not put secrets in it.

## Tells TapFun that the game has started. Send it once, when the game is ready to play.
static func launch_success_notice() -> Dictionary:
	return {"req": "launchSuccessNotice"}


## Asks for the player's environment. The answer has from=getPlayerEnvironment,
## userAgent, isSP, authenticated, isPaidTimer, language and country.
static func get_player_environment() -> Dictionary:
	return {"req": "getPlayerEnvironment"}


## Opens a URL in a new tab of the player's browser.
static func open_url(url: String) -> Dictionary:
	return {"req": "openUrl", "url": url}


## Shows a single-line text input. The answer has from=inputTextSingleLine, label,
## result (submit or cancel) and text. options can have title, text, placeholder,
## submit, cancel, maxlength, position, pattern and invalid.
static func input_text_single_line(label: String, options := {}) -> Dictionary:
	var request := {"req": "inputTextSingleLine", "label": label}
	request.merge(options)
	return request


## Shows a multi-line text input. The answer arrives through input_text_multi_line_received.
## options can have title, text, placeholder, submit, cancel, maxlength (up to 10000),
## position and end (the number of chunks sent with input_text_multi_line_chunks()).
static func input_text_multi_line(label: String, options := {}) -> Dictionary:
	var request := {"req": "inputTextMultiLine", "label": label}
	request.merge(options)
	return request


## Shows text in a modal.
static func show_text(text: String) -> Dictionary:
	return {"req": "showText", "text": text}


## Shows a modal that lets the player copy text to the clipboard.
static func copy_text(text: String, title := "") -> Dictionary:
	var request := {"req": "copyText", "text": text}
	if title != "":
		request["title"] = title
	return request


## Writes a log line in the player's browser. Open the game page with ?debugLog=1 to
## see it on screen, or ?verbose=1 to see it in the developer tools console.
static func console_log(data: String) -> Dictionary:
	return {"req": "consoleLog", "data": data}


# --- Web API. Each returns {"status": int, "body": String}; the body is JSON. ---

## Saves an image to the player's TapFun photos: POST /api/v1/photos (multipart/form-data).
## PNG, JPEG or WebP, up to 10 MB.
func upload_photo(image: PackedByteArray, file_name: String, content_type: String) -> Dictionary:
	var boundary := "----TapFunBoundary%d" % Time.get_ticks_usec()
	var body := PackedByteArray()
	body.append_array(("--%s\r\nContent-Disposition: form-data; name=\"file\"; filename=\"%s\"\r\nContent-Type: %s\r\n\r\n"
		% [boundary, file_name, content_type]).to_utf8_buffer())
	body.append_array(image)
	body.append_array(("\r\n--%s--\r\n" % boundary).to_utf8_buffer())
	var url := "%s/photos?userId=%s" % [_base_url(), user_id.uri_encode()]
	return await _http(HTTPClient.METHOD_POST, url, "multipart/form-data; boundary=" + boundary, body)


## Gets leaderboard entries: GET /api/v1/leaderboard/entries. Read them with
## JSON.parse_string(result.body).entries. options can have sort ("asc"), arround (1-100,
## entries around this player; the parameter name is "arround"), offset and limit.
func get_leaderboard_entries(name: String, options := {}) -> Dictionary:
	var query := {"name": name}
	query.merge(options)
	if query.has("arround"):
		query["userId"] = user_id
	var url := "%s/leaderboard/entries?%s" % [_base_url(), encode_query(query)]
	return await _http(HTTPClient.METHOD_GET, url, "", PackedByteArray())


## Saves this player's score: POST /api/v1/leaderboard/entries. A score less than or equal
## to the saved one is ignored unless force is true. The leaderboard is created on the first entry.
## name: 1 to 64 letters, digits, "_" and "-". note: up to 8192 characters.
func post_leaderboard_entry(name: String, score: int, note := "", force := false) -> Dictionary:
	var fields := {"name": name, "userId": user_id, "score": str(score)}
	if note != "":
		fields["note"] = note
	if force:
		fields["force"] = "1"
	var url := "%s/leaderboard/entries" % _base_url()
	return await _http(HTTPClient.METHOD_POST, url, "application/x-www-form-urlencoded", encode_query(fields).to_utf8_buffer())


## Deletes this player's entry: POST /api/v1/leaderboard/entries/delete. Succeeds even if there is none.
func delete_leaderboard_entry(name: String) -> Dictionary:
	var url := "%s/leaderboard/entries/delete" % _base_url()
	var fields := {"name": name, "userId": user_id}
	return await _http(HTTPClient.METHOD_POST, url, "application/x-www-form-urlencoded", encode_query(fields).to_utf8_buffer())


# --- Helpers ---

func _base_url() -> String:
	return "https://%s/api/v1" % api_host


func _http(method: HTTPClient.Method, url: String, content_type: String, body: PackedByteArray) -> Dictionary:
	var http := HTTPRequest.new()
	add_child(http)
	var headers := PackedStringArray([
		"Authorization: Bearer " + bearer_token,
		"User-Agent: " + user_agent,
	])
	if content_type != "":
		headers.append("Content-Type: " + content_type)
	var err := http.request_raw(url, headers, method, body)
	var result := {"status": 0, "body": error_string(err)}
	if err == OK:
		# [result, response_code, headers, body]
		var response: Array = await http.request_completed
		result.status = response[1] if response[0] == HTTPRequest.RESULT_SUCCESS else 0
		result.body = (response[3] as PackedByteArray).get_string_from_utf8()
	http.queue_free()
	return result


## Parses "--name value" and "--name=value". When an option is empty, TapFun drops it
## together with its name, so a missing option means "no value".
static func parse_args(list: PackedStringArray) -> Dictionary:
	var result := {}
	var i := 0
	while i < list.size():
		var arg := list[i]
		if arg.begins_with("-"):
			var eq := arg.find("=")
			if eq > 0:
				result[arg.substr(0, eq)] = arg.substr(eq + 1)
			elif i + 1 < list.size() and not list[i + 1].begins_with("--"):
				result[arg] = list[i + 1]
				i += 1
			else:
				result[arg] = ""
		i += 1
	return result


static func encode_query(fields: Dictionary) -> String:
	var parts := PackedStringArray()
	for key in fields:
		parts.append(str(key).uri_encode() + "=" + str(fields[key]).uri_encode())
	return "&".join(parts)


static func decode_query(query: String) -> Dictionary:
	var result := {}
	for pair in query.split("&", false):
		var kv := pair.split("=", true, 1)
		var value := kv[1] if kv.size() > 1 else ""
		result[kv[0].replace("+", " ").uri_decode()] = value.replace("+", " ").uri_decode()
	return result
