extends Control
## Buttons on the left, log on the right. The buttons can also be pressed with the A to L keys.
##
## What it does:
##   1. Reads the launch arguments (TapFunClient._ready).
##   2. Listens for messages from TapFun on UDP 127.0.0.1:{port}.
##   3. Sends launchSuccessNotice, then getPlayerEnvironment (sent again until the answer arrives).
##   4. Calls every other client API and Web API from the buttons and shows the answers.

const LEADERBOARD := "sample"

var tapfun := TapFunClient.new()
var log_view := TextEdit.new()
var actions := [
	# Client API
	["getPlayerEnvironment", func() -> void: tapfun.send(TapFunClient.get_player_environment())],
	["openUrl", func() -> void: tapfun.send(TapFunClient.open_url("https://tapfun.co.jp/"))],
	["inputTextSingleLine", func() -> void: tapfun.send(TapFunClient.input_text_single_line(
		"nickname", {"title": "Nickname", "placeholder": "Enter your nickname", "maxlength": "12"}))],
	["inputTextMultiLine", func() -> void: tapfun.send(TapFunClient.input_text_multi_line(
		"message", {"title": "Message", "text": "Line 1\nLine 2", "maxlength": "1000"}))],
	["inputTextMultiLine (long text)", _input_long_text],
	["showText", func() -> void: tapfun.send(TapFunClient.show_text("Hello from the game."))],
	["copyText", func() -> void: tapfun.send(TapFunClient.copy_text("SAMPLE-CODE-1234", "Your code"))],
	["consoleLog", func() -> void: tapfun.send(TapFunClient.console_log(
		JSON.stringify({"time": Time.get_datetime_string_from_system(true)})))],
	# Web API
	["photos (screenshot)", _upload_screenshot],
	["leaderboard: post score", _post_score],
	["leaderboard: get top 10", _get_top_scores],
	["leaderboard: delete mine", func() -> void: _log_result("leaderboard delete", await tapfun.delete_leaderboard_entry(LEADERBOARD))],
]


func _ready() -> void:
	_build_ui()
	add_child(tapfun)  # Reads the launch arguments.
	tapfun.message_received.connect(_on_message)
	tapfun.input_text_multi_line_received.connect(func(label: String, result: String, text: String) -> void:
		# The multi-line answer comes in chunks; this is called when all of them have arrived.
		_log("   entered (%s, %d chars): \"%s\"" % [label, text.length(), text] if result == "submit" else "   %s (%s)" % [result, label]))
	tapfun.request_completed.connect(func(req: String, status: int, body: String) -> void:
		_log("-> %s: HTTP %d %s" % [req, status, body]))

	# 1. Launch arguments. Do not show the TapFun user ID or the Bearer token on screen.
	_log("apiHost=%s port=%d userId=%s bearerToken=%s" % [
		tapfun.api_host, tapfun.port, _mask(tapfun.user_id), _mask(tapfun.bearer_token)])
	_log("username=%s isPaidTimer=%s isSp=%s dlcs=[%s]" % [
		str(tapfun.args.get("--username", "")).uri_decode(), tapfun.args.get("--is-paid-timer", ""),
		tapfun.args.get("--is-sp", ""), tapfun.args.get("--dlcs", "")])
	_log("language=%s country=%s" % [tapfun.language, tapfun.country])
	if not tapfun.has_client_api_values():
		_log("--tapfun-user-id, --api-host and --bearer-token are required. Add {userId}, {apiHost} and {bearerToken} to the exe launch parameters.")
		return

	# 2. Messages from TapFun.
	var err := tapfun.listen()
	_log("listening on UDP 127.0.0.1:%d" % tapfun.port if err == OK else "UDP listen failed: %s" % error_string(err))

	# 3. Startup requests. Send launchSuccessNotice once the game is ready to play.
	await tapfun.send(TapFunClient.launch_success_notice())

	# Answers to requests sent soon after the game starts can be lost: wait until STARTUP_WAIT_SEC has passed
	# since the start. fetch_player_environment() sends the request again when no answer arrives.
	var wait := TapFunClient.STARTUP_WAIT_SEC - Time.get_ticks_msec() / 1000.0
	if wait > 0:
		await get_tree().create_timer(wait).timeout
	if (await tapfun.fetch_player_environment()).is_empty():
		_log("getPlayerEnvironment: no answer")


func _on_message(message: Dictionary) -> void:
	var text := " ".join(message.keys().map(func(k: String) -> String: return "%s=%s" % [k, message[k]]))
	if message.has("from"):
		# Answer to a request: from=getPlayerEnvironment, from=inputTextSingleLine, ...
		_log("<- %s: %s" % [message["from"], text])
		if message["from"] == "inputTextSingleLine" and message.get("result", "") == "submit":
			_log("   entered: \"%s\"" % message.get("text", ""))
	elif message.get("req", "") == "clientStatus":
		# Sent by TapFun without a request, for example result=lastTimeRemain&second=90.
		_log("<- clientStatus: %s" % text)
	else:
		_log("<- %s" % text)


# inputTextMultiLine with an initial text too long for one request:
# send it in inputTextMultiLineChunkText requests first, then open the input with end = number of chunks.
func _input_long_text() -> void:
	var lines := PackedStringArray()
	for i in range(1, 31):
		lines.append("Line %d: this initial text is sent in chunks." % i)
	var chunks := TapFunClient.input_text_multi_line_chunks("\n".join(lines))
	for chunk in chunks:
		await tapfun.send(chunk)
	tapfun.send(TapFunClient.input_text_multi_line("long", {"title": "Long text", "maxlength": "2000", "end": str(chunks.size())}))


# Saves a screenshot of the game screen to the player's TapFun photos.
func _upload_screenshot() -> void:
	await RenderingServer.frame_post_draw
	var png := get_viewport().get_texture().get_image().save_png_to_buffer()
	_log_result("photos", await tapfun.upload_photo(png, "screenshot.png", "image/png"))


func _post_score() -> void:
	var score := randi_range(1, 9999)
	_log("   score=%d" % score)
	_log_result("leaderboard post", await tapfun.post_leaderboard_entry(LEADERBOARD, score, "from TapFunSample"))


func _get_top_scores() -> void:
	var result := await tapfun.get_leaderboard_entries(LEADERBOARD, {"limit": 10})
	var data = JSON.parse_string(result.body)
	if result.status != 200 or not data is Dictionary:
		_log_result("leaderboard get", result)
		return
	# The body has the user IDs of other players. Do not show them on screen: show names and scores.
	var entries: Array = data.get("entries", [])
	_log("-> leaderboard get: HTTP %d, %d entries" % [result.status, entries.size()])
	for entry in entries:
		var me := " (you)" if entry.userId == tapfun.user_id else ""
		_log("   #%d %s%s %d %s" % [int(entry.position), entry.userName, me, int(entry.score), entry.get("note", "")])


func _log_result(name: String, result: Dictionary) -> void:
	_log("-> %s: HTTP %d %s" % [name, result.status, result.body])


func _unhandled_key_input(event: InputEvent) -> void:
	var key := event as InputEventKey
	if key == null or not key.pressed or key.echo or key.get_modifiers_mask() != 0:
		return
	var index := key.keycode - KEY_A
	if index >= 0 and index < actions.size():
		_run(index)
		get_viewport().set_input_as_handled()


func _run(index: int) -> void:
	if not tapfun.has_client_api_values():
		_log("not sent: launch arguments are missing")
		return
	actions[index][1].call()


func _build_ui() -> void:
	var row := HBoxContainer.new()
	row.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	add_child(row)
	var buttons := VBoxContainer.new()
	buttons.custom_minimum_size = Vector2(340, 0)
	row.add_child(buttons)
	for i in actions.size():
		var button := Button.new()
		button.text = "%s) %s" % [char(KEY_A + i), actions[i][0]]
		button.alignment = HORIZONTAL_ALIGNMENT_LEFT
		button.custom_minimum_size = Vector2(0, 44)
		button.pressed.connect(_run.bind(i))
		buttons.add_child(button)
	log_view.editable = false
	log_view.wrap_mode = TextEdit.LINE_WRAPPING_BOUNDARY
	log_view.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	row.add_child(log_view)


func _log(line: String) -> void:
	var time := Time.get_time_string_from_system()
	print(line)  # Also goes to user://logs/godot.log.
	log_view.text += "%s %s\n" % [time, line]
	log_view.scroll_vertical = log_view.get_line_count()


func _mask(value: String) -> String:
	return "(none)" if value == "" else "***"
