var DROPBOX_REDIRECT_URI = "https://green-moss-0d6d06110.azurestaticapps.net";
var DROPBOX_CLIENT_ID = "umsfw8gmr6iy9hd";
var dbxAuth = new Dropbox.DropboxAuth({
  clientId: DROPBOX_CLIENT_ID,
});
var dbx = new Dropbox.Dropbox({
  auth: dbxAuth,
});
let entries;
// Parses the url and gets the access token if it is in the urls hash
function getCodeFromUrl() {
  var url = new URL(window.location);
  var query_string = url.search;
  var search_params = new URLSearchParams(query_string);
  return search_params.get("code");
}
// If the user was just redirected from authenticating, the urls hash will
// contain the access token.
function hasRedirectedFromAuth() {
  return !!getCodeFromUrl();
}

async function getUploadDrawingLink() {
  if (dbx) {
    let res = await dbx
      .filesGetTemporaryUploadLink({
        commit_info: {
          path: "/result.dwg",
          mode: "add",
          autorename: true,
          mute: false,
          strict_conflict: false,
        },
        duration: 3600,
      })
      .catch((err) => {
        console.error(err);
      });
    if (res) return res;
  }
}
// Render a list of items to #files
function renderTree(items) {
  console.log("object: %O", items);
  var nodes = []; // store the nodes
  computeNodes(items, nodes);
  console.log(nodes);
  $("#jstree")
    .jstree({
      core: {
        // core config object
        mulitple: true,
        animation: 100, // 200ms is default value
        check_callback: false, // this make contextmenu plugin to work
        themes: {
          variant: "large",
          dots: true,
        },
        data: nodes, // data core options end
      }, // core end

      // Types plugin
      types: {
        root: {
          icon: "fas fa-folder",
        },
        child: {
          icon: "fas fa-file",
        },
      },
      checkbox: {
        keep_selected_style: false,
        three_state: false, // to avoid that fact that checking a node also check others
        whole_node: false, // to avoid checking the box just clicking the node
        tie_selection: false, // for checking without selecting and selecting without checking
      },
      // injecting plugins
      plugins: ["ui", "unique", "types", "checkbox"],
    })
    .on("check_node.jstree uncheck_node.jstree", (e, data) => {
      var result = search(data.node.id, entries);
      dbx
        .filesGetTemporaryLink({
          path: result.path_display,
        })
        .then((res) => {
          Drawings.push(res.result.link);
        })
        .catch((e) => console.error(e));
    });
}
function renderTree2(items) {
  Drawings = [];
  var nodes = []; // store the nodes
  computeNodes(items, nodes);
  $("#jstree2")
    .jstree({
      core: {
        // core config object
        mulitple: true,
        animation: 100, // 200ms is default value
        check_callback: false, // this make contextmenu plugin to work
        themes: {
          variant: "large",
          dots: true,
        },
        data: nodes, // data core options end
      }, // core end

      // Types plugin
      types: {
        root: {
          icon: "fas fa-folder",
        },
        child: {
          icon: "fas fa-file",
        },
      },
      checkbox: {
        keep_selected_style: false,
        three_state: false, // to avoid that fact that checking a node also check others
        whole_node: false, // to avoid checking the box just clicking the node
        tie_selection: false, // for checking without selecting and selecting without checking
      },
      // injecting plugins
      plugins: ["ui", "unique", "types", "checkbox"],
    })
    .on("check_node.jstree uncheck_node.jstree", (e, data) => {
      var result = search(data.node.id, entries);
      dbx
        .filesGetTemporaryLink({
          path: result.path_display,
        })
        .then((res) => {
          Drawings.push(res.result.link);
        })
        .catch((e) => console.error(e));
    });
}
function search(nameKey, myArray) {
  for (var i = 0; i < myArray.length; i++) {
    if (myArray[i].id === nameKey) {
      return myArray[i];
    }
  }
}
function computeNodes(items, nodes) {
  var parents = {};
  let parentId = undefined;
  items.forEach((entry) => {
    // add nodes to array - you will also need id for every node
    // to properly map files to folders in the tree
    if (entry[".tag"] == "folder") {
      let levels = entry.path_display.split("/");
      let depth = levels.length;
      switch (depth) {
        case 2:
          nodes.push({
            id: entry.id,
            text: entry.name,
            parent: "#",
            type: "root",
          });
          break;
        case 3:
          nodes.push({
            id: entry.id,
            text: entry.name,
            parent: parents[levels[1]],
            type: "root",
          });
          break;
      }
      parents[entry.name] = entry.id;
    }

    if (entry[".tag"] == "file") {
      const folders = entry.path_display.split("/");
      switch (folders.length) {
        case 2:
          nodes.push({
            id: entry.id,
            text: entry.name,
            parent: "#",
            type: "child",
          });
          break;
        case 3:
          parentId = parents[folders[1]];
          nodes.push({
            id: entry.id,
            text: entry.name,
            parent: parentId,
            type: "child",
          });
          break;
        case 4:
          parentId = parents[folders[2]];
          nodes.push({
            id: entry.id,
            text: entry.name,
            parent: parentId,
            type: "child",
          });
          break;
      }
    }
  });
}
//Login to dropbox
async function doAuth() {
  const authUrl = await dbxAuth
    .getAuthenticationUrl(
      DROPBOX_REDIRECT_URI,
      undefined,
      "code",
      "offline",
      undefined,
      undefined,
      true
    )
    .catch((e) => {
      console.error(e);
    });
  if (!authUrl) return;
  window.sessionStorage.setItem("codeVerifier", dbxAuth.codeVerifier);
  window.location.href = authUrl;
}

let refreshListOfFiles = async (dbxAuth) => {
  response = await dbx
    .filesListFolder({
      path: "",
      recursive: true,
    })
    .catch((e) => {
      console.error(e);
    });
  if (!response) return;
  if (!response.result.has_more) {
    entries = response.result.entries;
  }
  return entries;
};
async function refreshTree() {
  let items = await refreshListOfFiles(dbxAuth).catch((e) => {
    console.error(e);
  });
  $("#jstree").css("display", "none");
  renderTree2(items);
  $("#jstree2").css("display", "block");
}
//Start the show
let start = async () => {
  if (hasRedirectedFromAuth()) {
    dbxAuth.setCodeVerifier(window.sessionStorage.getItem("codeVerifier"));
    let response = await dbxAuth
      .getAccessTokenFromCode(DROPBOX_REDIRECT_URI, getCodeFromUrl())
      .catch((e) => {
        console.error(e);
      });
    if (!response) return;
    dbxAuth.setAccessToken(response.result.access_token);
    entries = await refreshListOfFiles(dbxAuth).catch((e) => {
      console.error(e);
    });
    if (!entries) return;
    console.log("object: %O", entries);
    renderTree(entries);
  }
};
start();
