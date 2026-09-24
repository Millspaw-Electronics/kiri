// Start page of the KiRI Windows app. The app sends state and progress as
// messages; the page sends the user's choices back as { action: ... }.

"use strict";

function send(message) {
	window.chrome.webview.postMessage(message);
}

function el(id) {
	return document.getElementById(id);
}

function show(section) {
	for (const id of ["home", "choose", "busy"]) {
		el(id).hidden = (id !== section);
	}
}

function hide(id) {
	el(id).hidden = true;
}

function showError(text) {
	el("error-text").textContent = text;
	el("error").hidden = false;
}

// Creates an element with text content (never HTML, since paths and
// commit messages can contain anything)
function make(tag, className, text) {
	const node = document.createElement(tag);
	if (className) node.className = className;
	if (text !== undefined) node.textContent = text;
	return node;
}

function button(text, className, onclick) {
	const node = make("button", className, text);
	node.type = "button";
	node.onclick = (event) => { event.stopPropagation(); onclick(); };
	return node;
}

function renderRecent(recent) {
	const list = el("recent");
	list.replaceChildren();
	el("recent-card").hidden = recent.length === 0;

	for (const project of recent) {
		const item = make("li", "list-group-item list-group-item-action recent-item" + (project.exists ? "" : " missing"));
		item.title = project.exists ? project.path : project.path + " (not found)";
		const names = make("div", "names");
		names.append(make("div", "", project.name), make("div", "folder text-muted small", project.folder));
		item.append(names, button("×", "remove", () => send({ action: "remove_recent", path: project.path })));
		item.onclick = () => send({ action: "open_project", path: project.path });
		list.append(item);
	}
}

function renderTool(id, name, tool, missingHelp) {
	const item = el(id);
	item.replaceChildren();

	const details = make("div", "details");
	const title = make("div");
	title.append(make("span", tool.path ? "ok" : "missing", tool.path ? "✔ " : "✖ "), make("strong", "", name));
	if (tool.version) title.append(make("span", "text-muted", " " + tool.version));
	details.append(title);
	details.append(make("div", "path text-muted small", tool.path || missingHelp));
	item.append(details);

	if (tool.custom) {
		item.append(button("Use default", "btn btn-sm btn-outline-secondary ml-2", () => send({ action: "reset_tool", tool: id.replace("tool-", "") })));
	}
	item.append(button("Change…", "btn btn-sm btn-outline-light ml-2", () => send({ action: "set_tool", tool: id.replace("tool-", "") })));
}

function addLog(text, isError) {
	const log = el("busy-log");
	const line = make("div", isError ? "err" : "", text);
	log.append(line);
	log.scrollTop = log.scrollHeight;
}

function setProgress(fraction) {
	const bar = el("busy-bar");
	if (fraction === null || fraction === undefined) {
		bar.style.width = "100%";
		bar.classList.add("progress-bar-animated");
	} else {
		bar.style.width = Math.round(fraction * 100) + "%";
	}
}

window.chrome.webview.addEventListener("message", (event) => {
	const message = event.data;
	switch (message.type) {

		case "state":
			el("version").textContent = message.version;
			for (const node of document.querySelectorAll(".kicad-min")) node.textContent = message.kicad.min;
			renderRecent(message.recent);
			renderTool("tool-git", "Git", message.git, "Not found. Install Git for Windows or GitHub Desktop.");
			renderTool("tool-kicad", "KiCad", message.kicad, "Not found. Install KiCad " + message.kicad.min + " or newer.");
			el("open-btn").disabled = !message.git.path || !message.kicad.path;
			if (!message.busy) show("home");
			break;

		case "choose": {
			const list = el("choose-list");
			list.replaceChildren();
			for (const project of message.projects) {
				const item = make("li", "list-group-item list-group-item-action", project.name);
				item.style.cursor = "pointer";
				item.title = project.path;
				item.onclick = () => send({ action: "open_project", path: project.path });
				list.append(item);
			}
			hide("error");
			show("choose");
			break;
		}

		case "busy":
			el("busy-project").textContent = message.project;
			el("busy-folder").textContent = message.folder;
			el("busy-log").replaceChildren();
			setProgress(null);
			hide("error");
			show("busy");
			break;

		case "progress":
			addLog(message.message, message.isError);
			if (message.fraction !== null && message.fraction !== undefined) setProgress(message.fraction);
			break;

		case "error":
			show("home");
			showError(message.message);
			break;
	}
});

send({ action: "ready" });
